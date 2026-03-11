#!/usr/bin/env python3
"""Gemini V26 trade log analyzer.

Scans trade CSV logs under folders named "Trades", consolidates data, computes
system/instrument/group statistics, writes CSV+Excel reports, and renders charts.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Dict, Iterable, List, Optional

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

# Expected schema from Gemini trade logger
EXPECTED_COLUMNS: List[str] = [
    "CloseTimestamp",
    "Symbol",
    "PositionId",
    "Direction",
    "EntryType",
    "EntryReason",
    "MetaStatus",
    "EntryTime",
    "ExitTime",
    "EntryPrice",
    "ExitPrice",
    "VolumeInUnits",
    "EntryVolumeInUnits",
    "Tp1ClosedVolumeInUnits",
    "RemainingVolumeInUnits",
    "Confidence",
    "Tp1Hit",
    "Tp2Hit",
    "RiskPercent",
    "SlAtrMult",
    "Tp1R",
    "Tp2R",
    "LotCapHit",
    "BeActivated",
    "TrailingActivated",
    "ExitMode",
    "ExitReason",
    "NetProfit",
    "GrossProfit",
    "Commissions",
    "Swap",
    "Pips",
]

DATETIME_COLUMNS = ["CloseTimestamp", "EntryTime", "ExitTime"]
NUMERIC_COLUMNS = [
    "EntryPrice",
    "ExitPrice",
    "VolumeInUnits",
    "EntryVolumeInUnits",
    "Tp1ClosedVolumeInUnits",
    "RemainingVolumeInUnits",
    "Confidence",
    "RiskPercent",
    "SlAtrMult",
    "Tp1R",
    "Tp2R",
    "NetProfit",
    "GrossProfit",
    "Commissions",
    "Swap",
    "Pips",
]
BOOLEAN_COLUMNS = ["Tp1Hit", "Tp2Hit", "LotCapHit", "BeActivated", "TrailingActivated"]


def find_trade_csv_files(root: Path) -> List[Path]:
    """Find CSV files that are under any folder named Trades and not under Bars/Events."""
    files: List[Path] = []
    for path in root.rglob("*.csv"):
        parts = set(path.parts)
        if "Trades" in parts and "Bars" not in parts and "Events" not in parts:
            files.append(path)
    return sorted(files)


def parse_bool(value) -> Optional[bool]:
    if pd.isna(value):
        return pd.NA
    if isinstance(value, (bool, np.bool_)):
        return bool(value)
    txt = str(value).strip().lower()
    true_set = {"1", "true", "t", "yes", "y"}
    false_set = {"0", "false", "f", "no", "n"}
    if txt in true_set:
        return True
    if txt in false_set:
        return False
    return pd.NA


def ensure_columns(df: pd.DataFrame, expected_columns: Iterable[str]) -> pd.DataFrame:
    for col in expected_columns:
        if col not in df.columns:
            df[col] = pd.NA
    return df


def load_trade_data(csv_files: List[Path]) -> pd.DataFrame:
    """Load CSV files robustly; skip corrupted files."""
    frames: List[pd.DataFrame] = []
    skipped = 0

    for file in csv_files:
        try:
            frame = pd.read_csv(file, low_memory=False)
            frame["SourceFile"] = str(file)
            frames.append(frame)
        except Exception as exc:  # intentionally broad for robustness
            skipped += 1
            print(f"[WARN] Skipping corrupted/unreadable CSV: {file} ({exc})")

    if skipped:
        print(f"[INFO] Skipped {skipped} corrupted CSV file(s).")

    if not frames:
        return pd.DataFrame(columns=EXPECTED_COLUMNS + ["SourceFile"])

    combined = pd.concat(frames, ignore_index=True)
    combined = ensure_columns(combined, EXPECTED_COLUMNS)
    return combined


def normalize_types(df: pd.DataFrame) -> pd.DataFrame:
    for col in DATETIME_COLUMNS:
        if col in df.columns:
            df[col] = pd.to_datetime(df[col], errors="coerce", utc=False)

    for col in NUMERIC_COLUMNS:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors="coerce")

    for col in BOOLEAN_COLUMNS:
        if col in df.columns:
            df[col] = df[col].map(parse_bool).astype("boolean")

    for col in ["Symbol", "EntryType", "ExitReason", "ExitMode", "Direction", "MetaStatus"]:
        if col in df.columns:
            df[col] = df[col].astype("string")

    return df


def add_derived_fields(df: pd.DataFrame) -> pd.DataFrame:
    net_profit = df["NetProfit"].fillna(0.0) if "NetProfit" in df.columns else pd.Series(0.0, index=df.index)
    df["EquityUSD"] = net_profit.cumsum()
    df["RunningPeak"] = df["EquityUSD"].cummax()
    df["DrawdownUSD"] = df["EquityUSD"] - df["RunningPeak"]

    if "EntryTime" in df.columns and "ExitTime" in df.columns:
        df["HoldMinutes"] = (df["ExitTime"] - df["EntryTime"]).dt.total_seconds() / 60.0
    else:
        df["HoldMinutes"] = np.nan

    if "NetProfit" in df.columns:
        df["Win"] = df["NetProfit"] > 0
        df["Loss"] = df["NetProfit"] < 0
    else:
        df["Win"] = False
        df["Loss"] = False

    if "Confidence" in df.columns:
        bins = [-np.inf, 30, 50, 70, np.inf]
        labels = ["<=30", "31-50", "51-70", ">70"]
        df["ConfidenceBucket"] = pd.cut(df["Confidence"], bins=bins, labels=labels)
    else:
        df["ConfidenceBucket"] = pd.NA

    if "CloseTimestamp" in df.columns:
        df = df.sort_values("CloseTimestamp", kind="mergesort").reset_index(drop=True)

    return df


def profit_factor(profits: pd.Series) -> float:
    gross_profit = profits[profits > 0].sum()
    gross_loss = profits[profits < 0].sum()
    if gross_loss == 0:
        return np.inf if gross_profit > 0 else np.nan
    return float(gross_profit / abs(gross_loss))


def safe_mean(series: pd.Series) -> float:
    values = series.dropna()
    if values.empty:
        return np.nan
    return float(values.mean())


def summarize_group(df: pd.DataFrame, group_col: str) -> pd.DataFrame:
    if group_col not in df.columns:
        return pd.DataFrame()

    grouped = df.groupby(group_col, dropna=False, observed=False)
    rows = []
    for key, g in grouped:
        row = {
            group_col: key,
            "TotalTrades": len(g),
            "Wins": int((g["NetProfit"] > 0).sum()) if "NetProfit" in g else 0,
            "Losses": int((g["NetProfit"] < 0).sum()) if "NetProfit" in g else 0,
            "Winrate": safe_mean(g["NetProfit"] > 0) * 100.0 if "NetProfit" in g else np.nan,
            "NetProfitUSD": float(g["NetProfit"].sum()) if "NetProfit" in g else np.nan,
            "GrossProfitUSD": float(g.loc[g["NetProfit"] > 0, "NetProfit"].sum()) if "NetProfit" in g else np.nan,
            "GrossLossUSD": float(g.loc[g["NetProfit"] < 0, "NetProfit"].sum()) if "NetProfit" in g else np.nan,
            "ProfitFactor": profit_factor(g["NetProfit"].dropna()) if "NetProfit" in g else np.nan,
            "AverageTradeUSD": safe_mean(g["NetProfit"]) if "NetProfit" in g else np.nan,
            "MedianTradeUSD": float(g["NetProfit"].median()) if "NetProfit" in g else np.nan,
            "AveragePips": safe_mean(g["Pips"]) if "Pips" in g else np.nan,
            "AverageConfidence": safe_mean(g["Confidence"]) if "Confidence" in g else np.nan,
            "AverageHoldMinutes": safe_mean(g["HoldMinutes"]) if "HoldMinutes" in g else np.nan,
            "TP1HitRate": safe_mean(g["Tp1Hit"].astype(float)) * 100.0 if "Tp1Hit" in g else np.nan,
            "TP2HitRate": safe_mean(g["Tp2Hit"].astype(float)) * 100.0 if "Tp2Hit" in g else np.nan,
            "BEActivationRate": safe_mean(g["BeActivated"].astype(float)) * 100.0 if "BeActivated" in g else np.nan,
            "TrailingActivationRate": safe_mean(g["TrailingActivated"].astype(float)) * 100.0
            if "TrailingActivated" in g
            else np.nan,
            "MaxDrawdownUSD": float(g["DrawdownUSD"].min()) if "DrawdownUSD" in g else np.nan,
        }
        rows.append(row)

    return pd.DataFrame(rows)


def system_stats(df: pd.DataFrame) -> pd.DataFrame:
    if df.empty:
        return pd.DataFrame([{"TotalTrades": 0}])

    profits = df["NetProfit"].dropna() if "NetProfit" in df.columns else pd.Series(dtype=float)
    stats = {
        "TotalTrades": len(df),
        "Wins": int((df["NetProfit"] > 0).sum()) if "NetProfit" in df else 0,
        "Losses": int((df["NetProfit"] < 0).sum()) if "NetProfit" in df else 0,
        "Winrate": float((df["NetProfit"] > 0).mean() * 100.0) if "NetProfit" in df else np.nan,
        "NetProfitUSD": float(df["NetProfit"].sum()) if "NetProfit" in df else np.nan,
        "GrossProfitUSD": float(df.loc[df["NetProfit"] > 0, "NetProfit"].sum()) if "NetProfit" in df else np.nan,
        "GrossLossUSD": float(df.loc[df["NetProfit"] < 0, "NetProfit"].sum()) if "NetProfit" in df else np.nan,
        "ProfitFactor": profit_factor(profits),
        "AverageTradeUSD": safe_mean(df["NetProfit"]) if "NetProfit" in df else np.nan,
        "MedianTradeUSD": float(df["NetProfit"].median()) if "NetProfit" in df else np.nan,
        "AveragePips": safe_mean(df["Pips"]) if "Pips" in df else np.nan,
        "AverageConfidence": safe_mean(df["Confidence"]) if "Confidence" in df else np.nan,
        "AverageHoldMinutes": safe_mean(df["HoldMinutes"]) if "HoldMinutes" in df else np.nan,
        "TP1HitRate": safe_mean(df["Tp1Hit"].astype(float)) * 100.0 if "Tp1Hit" in df else np.nan,
        "TP2HitRate": safe_mean(df["Tp2Hit"].astype(float)) * 100.0 if "Tp2Hit" in df else np.nan,
        "BEActivationRate": safe_mean(df["BeActivated"].astype(float)) * 100.0 if "BeActivated" in df else np.nan,
        "TrailingActivationRate": safe_mean(df["TrailingActivated"].astype(float)) * 100.0
        if "TrailingActivated" in df
        else np.nan,
        "MaxDrawdownUSD": float(df["DrawdownUSD"].min()) if "DrawdownUSD" in df else np.nan,
    }
    return pd.DataFrame([stats])


def daily_stats(df: pd.DataFrame) -> pd.DataFrame:
    if "CloseTimestamp" not in df.columns:
        return pd.DataFrame(columns=["Date", "Trades", "NetProfitUSD", "Winrate", "ProfitFactor"])

    temp = df.copy()
    temp["Date"] = temp["CloseTimestamp"].dt.date
    grouped = temp.groupby("Date", dropna=True)
    rows = []
    for key, g in grouped:
        rows.append(
            {
                "Date": key,
                "Trades": len(g),
                "NetProfitUSD": float(g["NetProfit"].sum()) if "NetProfit" in g else np.nan,
                "Winrate": safe_mean(g["NetProfit"] > 0) * 100.0 if "NetProfit" in g else np.nan,
                "ProfitFactor": profit_factor(g["NetProfit"].dropna()) if "NetProfit" in g else np.nan,
            }
        )

    return pd.DataFrame(rows)


def strip_timezone_from_datetimes(df: pd.DataFrame) -> pd.DataFrame:
    out = df.copy()
    for col in out.columns:
        if pd.api.types.is_datetime64tz_dtype(out[col]):
            out[col] = out[col].dt.tz_localize(None)
    return out


def write_csv_outputs(output_dir: Path, tables: Dict[str, pd.DataFrame]) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    for name, table in tables.items():
        table.to_csv(output_dir / name, index=False)


def write_excel_report(output_file: Path, sheets: Dict[str, pd.DataFrame]) -> None:
    with pd.ExcelWriter(output_file, engine="openpyxl") as writer:
        for sheet_name, data in sheets.items():
            strip_timezone_from_datetimes(data).to_excel(writer, sheet_name=sheet_name, index=False)


def plot_equity_drawdown(df: pd.DataFrame, output_dir: Path) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)

    if df.empty:
        for filename in ["equity_curve.png", "drawdown_curve.png"]:
            plt.figure(figsize=(10, 4))
            plt.title("No trades available")
            plt.tight_layout()
            plt.savefig(output_dir / filename, dpi=120)
            plt.close()
        return

    x = df["CloseTimestamp"] if "CloseTimestamp" in df.columns else np.arange(len(df))

    plt.figure(figsize=(12, 5))
    plt.plot(x, df["EquityUSD"], label="EquityUSD", color="tab:blue")
    plt.title("Equity Curve")
    plt.xlabel("CloseTimestamp")
    plt.ylabel("Equity (USD)")
    plt.grid(alpha=0.3)
    plt.legend()
    plt.tight_layout()
    plt.savefig(output_dir / "equity_curve.png", dpi=140)
    plt.close()

    plt.figure(figsize=(12, 5))
    plt.plot(x, df["DrawdownUSD"], label="DrawdownUSD", color="tab:red")
    plt.title("Drawdown Curve")
    plt.xlabel("CloseTimestamp")
    plt.ylabel("Drawdown (USD)")
    plt.grid(alpha=0.3)
    plt.legend()
    plt.tight_layout()
    plt.savefig(output_dir / "drawdown_curve.png", dpi=140)
    plt.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Analyze Gemini V26 trade CSV logs.")
    parser.add_argument(
        "logs_root",
        nargs="?",
        type=Path,
        default=Path("."),
        help="Root Logs directory to scan (default: current dir).",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("."),
        help="Output directory for CSV/Excel/chart files (default: current dir).",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    logs_root: Path = args.logs_root
    output_dir: Path = args.output_dir

    csv_files = find_trade_csv_files(logs_root)
    print(f"[INFO] Found {len(csv_files)} trade CSV file(s).")

    trades = load_trade_data(csv_files)
    trades = normalize_types(trades)
    trades = add_derived_fields(trades)

    sys_stats = system_stats(trades)
    by_symbol = summarize_group(trades, "Symbol")
    by_entry_type = summarize_group(trades, "EntryType")
    by_exit_reason = summarize_group(trades, "ExitReason")
    by_exit_mode = summarize_group(trades, "ExitMode")
    by_direction = summarize_group(trades, "Direction")
    by_meta_status = summarize_group(trades, "MetaStatus")
    by_confidence = summarize_group(trades, "ConfidenceBucket")
    by_day = daily_stats(trades)

    csv_tables = {
        "system_stats.csv": sys_stats,
        "instrument_stats.csv": by_symbol,
        "entrytype_stats.csv": by_entry_type,
        "exitreason_stats.csv": by_exit_reason,
        "exitmode_stats.csv": by_exit_mode,
        "direction_stats.csv": by_direction,
        "confidence_stats.csv": by_confidence,
        "daily_stats.csv": by_day,
        "master_trades.csv": trades,
    }
    write_csv_outputs(output_dir, csv_tables)

    excel_sheets = {
        "System": sys_stats,
        "BySymbol": by_symbol,
        "ByEntryType": by_entry_type,
        "ByExitReason": by_exit_reason,
        "ByExitMode": by_exit_mode,
        "ByDirection": by_direction,
        "ByMetaStatus": by_meta_status,
        "ByConfidence": by_confidence,
        "Daily": by_day,
        "Trades": trades,
    }
    write_excel_report(output_dir / "Gemini_Trading_Report.xlsx", excel_sheets)
    plot_equity_drawdown(trades, output_dir)

    row = sys_stats.iloc[0]
    print("\n=== Gemini Log Analysis Summary ===")
    print(f"Total trades analyzed: {int(row.get('TotalTrades', 0) or 0)}")
    print(f"Net profit: {row.get('NetProfitUSD', np.nan):.2f}")
    print(f"Winrate: {row.get('Winrate', np.nan):.2f}%")
    print(f"Profit factor: {row.get('ProfitFactor', np.nan):.4f}")
    print(f"Maximum drawdown: {row.get('MaxDrawdownUSD', np.nan):.2f}")


if __name__ == "__main__":
    main()

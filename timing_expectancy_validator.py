#!/usr/bin/env python3
"""Timing expectancy validator for Gemini V26 (stdlib-only).

Inputs:
  --after-csv  required
  --before-csv optional

The CSV is expected to contain the strategy export fields listed in the task.
Missing columns are tolerated (filled with empty values), with conservative fallbacks.
"""

from __future__ import annotations

import argparse
import csv
import math
from pathlib import Path
from statistics import median
from typing import Dict, Iterable, List, Optional, Tuple

REQUIRED_COLUMNS = [
    "Symbol",
    "EntryType",
    "SetupType",
    "MarketRegime",
    "RMultiple",
    "MfeR",
    "MaeR",
    "Confidence",
    "TimingPenalty",
    "TimingBlocked",
    "TimingOverride",
    "TimingReason",
    "Overextended",
    "Exhausted",
]

GROUP_NAMES = ["clean", "overridden_strict", "blocked_timing", "blocked_exhaustion"]


def parse_bool(value: str) -> Optional[bool]:
    if value is None:
        return None
    txt = str(value).strip().lower()
    if txt in {"1", "true", "t", "yes", "y"}:
        return True
    if txt in {"0", "false", "f", "no", "n"}:
        return False
    return None


def parse_float(value: str) -> Optional[float]:
    if value is None:
        return None
    txt = str(value).strip()
    if txt == "":
        return None
    try:
        return float(txt)
    except ValueError:
        return None


def load_csv(path: Path) -> List[Dict[str, str]]:
    with path.open("r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        rows = []
        for row in reader:
            full = {k: row.get(k, "") for k in REQUIRED_COLUMNS}
            for k, v in row.items():
                if k not in full:
                    full[k] = v
            rows.append(full)
        return rows


def mean(values: Iterable[Optional[float]]) -> Optional[float]:
    nums = [v for v in values if v is not None and not math.isnan(v)]
    if not nums:
        return None
    return sum(nums) / len(nums)


def pct_true(values: Iterable[Optional[bool]]) -> Optional[float]:
    vals = [v for v in values if v is not None]
    if not vals:
        return None
    return 100.0 * sum(1 for v in vals if v) / len(vals)


def safe_ratio(a: Optional[float], b: Optional[float]) -> Optional[float]:
    if a is None or b is None or b == 0:
        return None
    return a / b


def fmt(x: Optional[float], digits: int = 4) -> str:
    if x is None or (isinstance(x, float) and math.isnan(x)):
        return ""
    return f"{x:.{digits}f}"


def penalty_bucket(p: Optional[float]) -> str:
    if p is None:
        return "NA"
    if 0 >= p > -5:
        return "0 to -5"
    if -5 >= p > -10:
        return "-5 to -10"
    if -10 >= p > -15:
        return "-10 to -15"
    if -15 >= p > -20:
        return "-15 to -20"
    if p <= -20:
        return "< -20"
    return "> 0"


def score_bucket(s: Optional[float]) -> str:
    if s is None:
        return "NA"
    if 60 <= s < 70:
        return "60-70"
    if 70 <= s < 80:
        return "70-80"
    if 80 <= s < 90:
        return "80-90"
    if s >= 90:
        return "90+"
    return "<60"


def penalty_band(p: Optional[float]) -> str:
    if p is None:
        return "NA"
    if p > -10:
        return "> -10"
    if -20 < p <= -10:
        return "-10 to -20"
    return "< -20"


def classify_group(r: Dict[str, str]) -> str:
    blocked = parse_bool(r.get("TimingBlocked"))
    override = parse_bool(r.get("TimingOverride"))
    overextended = parse_bool(r.get("Overextended"))
    exhausted = parse_bool(r.get("Exhausted"))
    reason = str(r.get("TimingReason", "")).strip().lower()
    score = parse_float(r.get("Confidence"))
    penalty = parse_float(r.get("TimingPenalty"))

    is_exhaustion = (overextended is True) or (exhausted is True)

    if blocked is False and override is False and not is_exhaustion:
        return "clean"
    if override is True and score is not None and score >= 80 and penalty is not None and penalty > -15:
        return "overridden_strict"
    if blocked is True and reason == "timing_block":
        return "blocked_timing"
    if is_exhaustion and reason == "exhaustion_block":
        return "blocked_exhaustion"
    return "other"


def enrich(rows: List[Dict[str, str]]) -> List[Dict[str, object]]:
    out = []
    for r in rows:
        rr: Dict[str, object] = dict(r)
        rr["RMultiple_f"] = parse_float(r.get("RMultiple"))
        rr["MfeR_f"] = parse_float(r.get("MfeR"))
        rr["MaeR_f"] = parse_float(r.get("MaeR"))
        rr["Confidence_f"] = parse_float(r.get("Confidence"))
        rr["TimingPenalty_f"] = parse_float(r.get("TimingPenalty"))

        rmult = rr["RMultiple_f"]
        rr["Win"] = (rmult is not None and rmult > 0)
        rr["BE"] = (rmult is not None and abs(rmult) <= 0.05)
        rr["FullSL"] = (rmult is not None and rmult <= -0.95)

        tp1 = parse_bool(r.get("Tp1Hit"))
        if tp1 is None:
            mfe = rr["MfeR_f"]
            rr["ReachedTP1"] = (mfe is not None and mfe >= 1.0)
            rr["Tp1Proxy"] = True
        else:
            rr["ReachedTP1"] = tp1
            rr["Tp1Proxy"] = False

        rr["TimingGroup"] = classify_group(r)
        rr["PenaltyBucket"] = penalty_bucket(rr["TimingPenalty_f"])
        rr["ScoreBucket"] = score_bucket(rr["Confidence_f"])
        rr["PenaltyBand"] = penalty_band(rr["TimingPenalty_f"])
        out.append(rr)
    return out


def summarize_group(rows: List[Dict[str, object]]) -> List[Dict[str, object]]:
    groups: Dict[str, List[Dict[str, object]]] = {}
    for r in rows:
        g = str(r["TimingGroup"])
        if g in GROUP_NAMES:
            groups.setdefault(g, []).append(r)

    metrics: List[Dict[str, object]] = []
    for g in GROUP_NAMES:
        rr = groups.get(g, [])
        if not rr:
            continue
        rvals = [x["RMultiple_f"] for x in rr]
        mfe = mean([x["MfeR_f"] for x in rr])
        mae = mean([x["MaeR_f"] for x in rr])
        med_vals = [v for v in rvals if v is not None]
        metrics.append(
            {
                "Group": g,
                "SampleSize": len(rr),
                "AvgR": mean(rvals),
                "MedianR": median(med_vals) if med_vals else None,
                "WinratePct": pct_true([bool(x["Win"]) for x in rr]),
                "AvgMFE": mfe,
                "AvgMAE": mae,
                "MFE_MAE_Ratio": safe_ratio(mfe, mae),
                "TP1Pct": pct_true([bool(x["ReachedTP1"]) for x in rr]),
                "BEPct": pct_true([bool(x["BE"]) for x in rr]),
                "FullSLPct": pct_true([bool(x["FullSL"]) for x in rr]),
            }
        )
    return metrics


def summarize_penalty_curve(rows: List[Dict[str, object]]) -> List[Dict[str, object]]:
    ordered = ["0 to -5", "-5 to -10", "-10 to -15", "-15 to -20", "< -20", "> 0", "NA"]
    by: Dict[str, List[Dict[str, object]]] = {}
    for r in rows:
        by.setdefault(str(r["PenaltyBucket"]), []).append(r)

    out = []
    for b in ordered:
        rr = by.get(b, [])
        if not rr:
            continue
        out.append(
            {
                "PenaltyBucket": b,
                "SampleSize": len(rr),
                "AvgR": mean([x["RMultiple_f"] for x in rr]),
                "WinratePct": pct_true([x["RMultiple_f"] is not None and x["RMultiple_f"] > 0 for x in rr]),
            }
        )
    return out


def summarize_matrix(rows: List[Dict[str, object]]) -> List[Dict[str, object]]:
    score_order = ["60-70", "70-80", "80-90", "90+", "<60", "NA"]
    pen_order = ["> -10", "-10 to -20", "< -20", "NA"]

    by: Dict[Tuple[str, str], List[Dict[str, object]]] = {}
    for r in rows:
        key = (str(r["ScoreBucket"]), str(r["PenaltyBand"]))
        by.setdefault(key, []).append(r)

    out = []
    for sb in score_order:
        for pb in pen_order:
            rr = by.get((sb, pb), [])
            if not rr:
                continue
            out.append(
                {
                    "ScoreBucket": sb,
                    "PenaltyBand": pb,
                    "SampleSize": len(rr),
                    "AvgR": mean([x["RMultiple_f"] for x in rr]),
                    "WinratePct": pct_true([x["RMultiple_f"] is not None and x["RMultiple_f"] > 0 for x in rr]),
                }
            )
    return out


def timing_edge_score(group_metrics: List[Dict[str, object]]) -> Optional[float]:
    lookup = {str(r["Group"]): r for r in group_metrics}
    c = lookup.get("clean", {}).get("AvgR")
    b = lookup.get("blocked_timing", {}).get("AvgR")
    if c is None or b is None:
        return None
    return c - b


def write_table(path: Path, rows: List[Dict[str, object]], columns: List[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=columns)
        w.writeheader()
        for r in rows:
            w.writerow({c: r.get(c, "") for c in columns})


def print_table(title: str, rows: List[Dict[str, object]], columns: List[str]) -> None:
    print(f"\n=== {title} ===")
    if not rows:
        print("(no rows)")
        return
    print(",".join(columns))
    for r in rows:
        print(",".join(fmt(r.get(c)) if isinstance(r.get(c), float) or r.get(c) is None else str(r.get(c)) for c in columns))


def run_dataset(name: str, csv_path: Path, out_dir: Path) -> Dict[str, object]:
    rows = enrich(load_csv(csv_path))

    group_metrics = summarize_group(rows)
    penalty_curve = summarize_penalty_curve(rows)
    matrix = summarize_matrix(rows)
    tes = timing_edge_score(group_metrics)

    write_table(out_dir / f"{name}_group_metrics.csv", group_metrics, [
        "Group", "SampleSize", "AvgR", "MedianR", "WinratePct", "AvgMFE", "AvgMAE", "MFE_MAE_Ratio", "TP1Pct", "BEPct", "FullSLPct",
    ])
    write_table(out_dir / f"{name}_penalty_curve.csv", penalty_curve, ["PenaltyBucket", "SampleSize", "AvgR", "WinratePct"])
    write_table(out_dir / f"{name}_score_penalty_matrix.csv", matrix, ["ScoreBucket", "PenaltyBand", "SampleSize", "AvgR", "WinratePct"])

    quality = [{
        "HasRMultiple": any(r["RMultiple_f"] is not None for r in rows),
        "HasMfeMae": any(r["MfeR_f"] is not None for r in rows) and any(r["MaeR_f"] is not None for r in rows),
        "Tp1ProxyUsed": any(bool(r["Tp1Proxy"]) for r in rows),
        "BEThresholdAbsR": 0.05,
        "FullSLThresholdR": -0.95,
    }]
    write_table(out_dir / f"{name}_rmultiple_checks.csv", quality, ["HasRMultiple", "HasMfeMae", "Tp1ProxyUsed", "BEThresholdAbsR", "FullSLThresholdR"])

    print_table(f"{name.upper()} GROUP METRICS", group_metrics, [
        "Group", "SampleSize", "AvgR", "MedianR", "WinratePct", "AvgMFE", "AvgMAE", "MFE_MAE_Ratio", "TP1Pct", "BEPct", "FullSLPct",
    ])
    print_table(f"{name.upper()} PENALTY CURVE", penalty_curve, ["PenaltyBucket", "SampleSize", "AvgR", "WinratePct"])
    print_table(f"{name.upper()} SCORE x PENALTY", matrix, ["ScoreBucket", "PenaltyBand", "SampleSize", "AvgR", "WinratePct"])
    print(f"\n{name.upper()} TES={fmt(tes)}")

    return {
        "rows": rows,
        "group_metrics": group_metrics,
        "tes": tes,
    }


def comparison(before: Dict[str, object], after: Dict[str, object]) -> List[Dict[str, object]]:
    bmap = {str(r["Group"]): r for r in before["group_metrics"]}
    amap = {str(r["Group"]): r for r in after["group_metrics"]}

    rows = []
    for g in GROUP_NAMES:
        br = bmap.get(g, {})
        ar = amap.get(g, {})
        b_avg = br.get("AvgR")
        a_avg = ar.get("AvgR")
        b_wr = br.get("WinratePct")
        a_wr = ar.get("WinratePct")
        rows.append(
            {
                "Group": g,
                "BeforeAvgR": b_avg,
                "AfterAvgR": a_avg,
                "DeltaAvgR": (a_avg - b_avg) if isinstance(a_avg, float) and isinstance(b_avg, float) else None,
                "BeforeWinratePct": b_wr,
                "AfterWinratePct": a_wr,
                "DeltaWinratePct": (a_wr - b_wr) if isinstance(a_wr, float) and isinstance(b_wr, float) else None,
            }
        )

    b_tes = before.get("tes")
    a_tes = after.get("tes")
    rows.append(
        {
            "Group": "TES",
            "BeforeAvgR": b_tes,
            "AfterAvgR": a_tes,
            "DeltaAvgR": (a_tes - b_tes) if isinstance(a_tes, float) and isinstance(b_tes, float) else None,
            "BeforeWinratePct": None,
            "AfterWinratePct": None,
            "DeltaWinratePct": None,
        }
    )
    return rows


def main() -> None:
    p = argparse.ArgumentParser(description="Validate updated timing logic expectancy using trade CSV exports.")
    p.add_argument("--after-csv", required=True, type=Path)
    p.add_argument("--before-csv", type=Path)
    p.add_argument("--output-dir", type=Path, default=Path("timing_validation_output"))
    args = p.parse_args()

    after = run_dataset("after", args.after_csv, args.output_dir)

    if args.before_csv:
        before = run_dataset("before", args.before_csv, args.output_dir)
        comp = comparison(before, after)
        write_table(args.output_dir / "before_vs_after_summary.csv", comp, [
            "Group", "BeforeAvgR", "AfterAvgR", "DeltaAvgR", "BeforeWinratePct", "AfterWinratePct", "DeltaWinratePct",
        ])
        print_table("BEFORE vs AFTER", comp, [
            "Group", "BeforeAvgR", "AfterAvgR", "DeltaAvgR", "BeforeWinratePct", "AfterWinratePct", "DeltaWinratePct",
        ])


if __name__ == "__main__":
    main()

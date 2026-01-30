using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core.Entry;

// HtfBiasModels.cs
namespace GeminiV26.Core.HtfBias
{
    public enum HtfBiasState
    {
        Neutral = 0,
        Bull = 1,
        Bear = 2,
        Transition = 3
    }
}

/*
public sealed class HtfBiasSnapshot
{
    public HtfBiasState State { get; set; } = HtfBiasState.Neutral;
    public TradeDirection AllowedDirection { get; set; } = TradeDirection.None;
    public double Confidence01 { get; set; } = 0.0;
    public DateTime LastUpdateH1Closed { get; set; } = DateTime.MinValue;
    public string Reason { get; set; } = "INIT";

    // convenience helpers (használhatók engine-ekben)
    public static HtfBiasSnapshot Neutral(string reason)
        => new() { State = HtfBiasState.Neutral, Reason = reason };

    public static HtfBiasSnapshot Transition(string reason)
        => new() { State = HtfBiasState.Transition, Reason = reason };

    public static HtfBiasSnapshot Trend(
        TradeDirection dir,
        double confidence,
        string reason)
        => new()
        {
            State = dir == TradeDirection.Long ? HtfBiasState.Bull : HtfBiasState.Bear,
            AllowedDirection = dir,
            Confidence01 = confidence,
            Reason = reason
        };
}
*/

public interface IHtfBiasProvider
    {
        HtfBiasSnapshot Get(string symbolName);
    }
}

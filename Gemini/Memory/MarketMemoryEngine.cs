using System;
using System.Collections.Generic;
using cAlgo.API;

namespace Gemini.Memory
{
    public sealed class MarketMemoryEngine
    {
        private const int StaleImpulseThresholdBars = 8;
        private const int ChaseRiskThresholdBars = 4;
        private readonly Action<string> _log;

        public Dictionary<string, SymbolMemoryState> States { get; } = new Dictionary<string, SymbolMemoryState>(StringComparer.OrdinalIgnoreCase);

        public MarketMemoryEngine(Action<string> log = null)
        {
            _log = log;
        }

        public SymbolMemoryState GetState(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Initialize(string.Empty);

            return States.TryGetValue(symbol, out var state)
                ? state
                : Initialize(symbol);
        }

        public SymbolMemoryState Initialize(string symbol)
        {
            string key = symbol?.Trim() ?? string.Empty;
            var state = new SymbolMemoryState
            {
                Symbol = key,
                MovePhase = MovePhase.Unknown,
                TrustLevel = MemoryTrustLevel.Unknown,
                BuildMode = MemoryBuildMode.Default
            };

            States[key] = state;
            return state;
        }

        public void BuildFromHistory(string symbol, List<Bar> bars)
        {
            var state = GetState(symbol);
            _log?.Invoke($"[MEMORY][BUILD_START] symbol={state.Symbol} bars={(bars?.Count ?? 0)}");

            state.MoveAgeBars = 0;
            state.PullbackCount = 0;
            state.BarsSinceImpulse = 0;
            state.IsStaleImpulse = false;
            state.IsImpulseDecay = false;
            state.MovePhase = MovePhase.Unknown;
            state.BuildMode = MemoryBuildMode.HistoricalReplay;
            state.TrustLevel = MemoryTrustLevel.Medium;

            if (bars == null || bars.Count == 0)
            {
                _log?.Invoke($"[MEMORY][DONE] symbol={state.Symbol} mode={state.BuildMode} phase={state.MovePhase} age={state.MoveAgeBars}");
                return;
            }

            foreach (var bar in bars)
            {
                ReplayBar(state, bar);
            }

            _log?.Invoke($"[MEMORY][DONE] symbol={state.Symbol} mode={state.BuildMode} phase={state.MovePhase} age={state.MoveAgeBars} pullbacks={state.PullbackCount} sinceImpulse={state.BarsSinceImpulse}");
        }

        public void OnBar(string symbol, Bar bar)
        {
            var state = GetState(symbol);
            if (state.BuildMode == MemoryBuildMode.Default)
            {
                state.BuildMode = MemoryBuildMode.Live;
                state.TrustLevel = MemoryTrustLevel.Low;
            }
            else if (state.BuildMode == MemoryBuildMode.HistoricalReplay)
            {
                state.BuildMode = MemoryBuildMode.Live;
                state.TrustLevel = MemoryTrustLevel.High;
            }

            state.MoveAgeBars++;
            state.BarsSinceImpulse++;

            if (IsStrongMove(bar))
            {
                state.MovePhase = MovePhase.Impulse;
                state.MoveAgeBars = 1;
                state.BarsSinceImpulse = 0;
                state.IsStaleImpulse = false;
                state.IsImpulseDecay = false;
                _log?.Invoke($"[MEMORY][UPDATE] symbol={state.Symbol} age={state.MoveAgeBars} sinceImpulse={state.BarsSinceImpulse}");
                _log?.Invoke($"[MEMORY][IMPULSE] symbol={state.Symbol} phase={state.MovePhase}");
                return;
            }

            if (IsRetrace(bar))
            {
                state.PullbackCount++;
                state.MovePhase = MovePhase.Pullback;
                _log?.Invoke($"[MEMORY][PULLBACK] symbol={state.Symbol} pullbacks={state.PullbackCount}");
            }
            else if (state.BarsSinceImpulse > 1)
            {
                state.MovePhase = MovePhase.Decay;
            }

            state.IsImpulseDecay = state.MovePhase == MovePhase.Decay;
            if (state.BarsSinceImpulse > StaleImpulseThresholdBars)
            {
                state.IsStaleImpulse = true;
                state.MovePhase = MovePhase.Stale;
            }

            _log?.Invoke($"[MEMORY][UPDATE] symbol={state.Symbol} age={state.MoveAgeBars} sinceImpulse={state.BarsSinceImpulse}");
        }

        public MemoryAssessment GetAssessment(string symbol)
        {
            var state = GetState(symbol);
            var assessment = new MemoryAssessment
            {
                IsLateMove = state.PullbackCount > 2 || state.IsStaleImpulse,
                IsChaseRisk = state.BarsSinceImpulse > ChaseRiskThresholdBars,
                ContextTrustScore = ResolveContextTrustScore(state.BuildMode),
                RecommendedPenalty = 0
            };

            if (assessment.IsLateMove)
                assessment.RecommendedPenalty -= 20;

            if (assessment.IsChaseRisk)
                assessment.RecommendedPenalty -= 15;

            _log?.Invoke($"[MEMORY][ASSESSMENT] symbol={state.Symbol} late={assessment.IsLateMove} chase={assessment.IsChaseRisk} trust={assessment.ContextTrustScore:0.##} penalty={assessment.RecommendedPenalty}");
            return assessment;
        }

        private void ReplayBar(SymbolMemoryState state, Bar bar)
        {
            state.MoveAgeBars++;
            state.BarsSinceImpulse++;
            _log?.Invoke($"[MEMORY][REPLAY] symbol={state.Symbol} age={state.MoveAgeBars} sinceImpulse={state.BarsSinceImpulse}");

            if (IsStrongMove(bar))
            {
                state.MovePhase = MovePhase.Impulse;
                state.MoveAgeBars = 1;
                state.BarsSinceImpulse = 0;
                state.IsStaleImpulse = false;
                state.IsImpulseDecay = false;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
                return;
            }

            if (IsRetrace(bar))
            {
                state.PullbackCount++;
                state.MovePhase = MovePhase.Pullback;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase} pullbacks={state.PullbackCount}");
            }
            else if (state.BarsSinceImpulse > 1)
            {
                state.MovePhase = MovePhase.Decay;
                state.IsImpulseDecay = true;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
            }

            if (state.BarsSinceImpulse > StaleImpulseThresholdBars)
            {
                state.IsStaleImpulse = true;
                state.MovePhase = MovePhase.Stale;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
            }
        }

        private static bool IsStrongMove(Bar bar)
        {
            if (bar == null)
                return false;

            double range = Math.Abs(bar.High - bar.Low);
            if (range <= 0)
                return false;

            double body = Math.Abs(bar.Close - bar.Open);
            return body >= range * 0.60;
        }

        private static bool IsRetrace(Bar bar)
        {
            if (bar == null)
                return false;

            double range = Math.Abs(bar.High - bar.Low);
            if (range <= 0)
                return false;

            double body = Math.Abs(bar.Close - bar.Open);
            return body <= range * 0.35;
        }

        private static double ResolveContextTrustScore(MemoryBuildMode buildMode)
        {
            return buildMode switch
            {
                MemoryBuildMode.HistoricalReplay => 0.70,
                MemoryBuildMode.Live => 0.90,
                _ => 0.30
            };
        }
    }
}

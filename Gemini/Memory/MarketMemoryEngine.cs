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

        public int TotalSymbolCount { get; private set; }
        public double MemoryCoverageRatio { get; private set; }

        public int BuiltSymbolCount => CountStates(state => state.IsBuilt);
        public int ResolvedSymbolCount => CountStates(state => state.IsResolved);
        public int UsableSymbolCount => CountStates(state => state.IsUsable);

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
                BuildMode = MemoryBuildMode.Default,
                IsBuilt = false,
                IsResolved = false,
                IsUsable = false,
                ResolveFailureReason = string.Empty
            };

            States[key] = state;
            return state;
        }

        public void MarkResolved(string symbol)
        {
            var state = GetState(symbol);
            state.IsResolved = true;
            state.ResolveFailureReason = string.Empty;
            RecomputeUsable(state);
        }

        public void MarkResolveFailure(string symbol, string reason)
        {
            var state = GetState(symbol);
            state.IsResolved = false;
            state.IsUsable = false;
            state.ResolveFailureReason = reason ?? string.Empty;
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
            state.HasActiveImpulse = false;
            state.ImpulseDirection = 0;
            state.LastImpulseHigh = 0;
            state.LastImpulseLow = 0;
            state.MovePhase = MovePhase.Unknown;
            state.BuildMode = MemoryBuildMode.HistoricalReplay;
            state.TrustLevel = MemoryTrustLevel.Medium;
            state.IsBuilt = false;

            if (bars == null || bars.Count == 0)
            {
                state.IsBuilt = true;
                RecomputeUsable(state);
                _log?.Invoke($"[MEMORY][REPLAY] symbol={state.Symbol} bars=0 reason=no_history");
                _log?.Invoke($"[MEMORY][DONE] symbol={state.Symbol} mode={state.BuildMode} phase={state.MovePhase} age={state.MoveAgeBars}");
                return;
            }

            foreach (var bar in bars)
            {
                ReplayBar(state, bar);
            }

            state.IsBuilt = true;
            RecomputeUsable(state);
            _log?.Invoke($"[MEMORY][DONE] symbol={state.Symbol} mode={state.BuildMode} phase={state.MovePhase} age={state.MoveAgeBars} pullbacks={state.PullbackCount} sinceImpulse={state.BarsSinceImpulse}");
        }

        public void OnBar(string symbol, Bar bar)
        {
            var state = GetState(symbol);
            state.IsBuilt = true;

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
                ApplyImpulse(state, bar, "new_impulse");
                RecomputeUsable(state);
                _log?.Invoke($"[MEMORY][UPDATE] symbol={state.Symbol} age={state.MoveAgeBars} sinceImpulse={state.BarsSinceImpulse}");
                _log?.Invoke($"[MEMORY][IMPULSE] symbol={state.Symbol} phase={state.MovePhase}");
                return;
            }

            if (HasTrendBreak(state, bar))
            {
                ResetPullback(state, "trend_break");
                state.MovePhase = MovePhase.Decay;
                state.BarsSinceImpulse = 0;
                state.HasActiveImpulse = false;
            }
            else if (HasContinuation(state, bar))
            {
                UpdateImpulseExtremes(state, bar);
                state.MovePhase = MovePhase.Continuation;
                state.IsImpulseDecay = false;
                state.IsStaleImpulse = false;
            }
            else if (IsEligiblePullback(state, bar))
            {
                IncrementPullback(state, "retrace_without_new_extreme");
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

            RecomputeUsable(state);
            _log?.Invoke($"[MEMORY][UPDATE] symbol={state.Symbol} age={state.MoveAgeBars} sinceImpulse={state.BarsSinceImpulse}");
        }

        public string GetCoverageRatio(IEnumerable<string> symbols)
        {
            var coverage = ComputeCoverage(symbols);
            TotalSymbolCount = coverage.Total;
            MemoryCoverageRatio = coverage.Total > 0
                ? (double)coverage.Built / coverage.Total
                : 0d;

            return $"{coverage.Built}/{coverage.Total}";
        }

        public string GetBuiltCoverageRatio(IEnumerable<string> symbols)
        {
            var coverage = ComputeCoverage(symbols);
            return FormatCoverage(coverage.Built, coverage.Total);
        }

        public string GetResolvedCoverageRatio(IEnumerable<string> symbols)
        {
            var coverage = ComputeCoverage(symbols);
            return FormatCoverage(coverage.Resolved, coverage.Total);
        }

        public string GetUsableCoverageRatio(IEnumerable<string> symbols)
        {
            var coverage = ComputeCoverage(symbols);
            return FormatCoverage(coverage.Usable, coverage.Total);
        }

        public MemoryAssessment GetAssessment(string symbol)
        {
            var state = GetState(symbol);
            var assessment = new MemoryAssessment
            {
                IsLateMove = state.PullbackCount > 2 || state.IsStaleImpulse,
                IsChaseRisk = state.BarsSinceImpulse > ChaseRiskThresholdBars,
                ContextTrustScore = state.IsUsable ? ResolveContextTrustScore(state.BuildMode) : 0.0,
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
                ApplyImpulse(state, bar, "new_impulse");
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
                return;
            }

            if (HasTrendBreak(state, bar))
            {
                ResetPullback(state, "trend_break");
                state.MovePhase = MovePhase.Decay;
                state.BarsSinceImpulse = 0;
                state.HasActiveImpulse = false;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
            }
            else if (HasContinuation(state, bar))
            {
                UpdateImpulseExtremes(state, bar);
                state.MovePhase = MovePhase.Continuation;
                state.IsImpulseDecay = false;
                state.IsStaleImpulse = false;
                _log?.Invoke($"[MEMORY][PHASE] symbol={state.Symbol} phase={state.MovePhase}");
            }
            else if (IsEligiblePullback(state, bar))
            {
                IncrementPullback(state, "retrace_without_new_extreme");
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

        private void ApplyImpulse(SymbolMemoryState state, Bar bar, string reason)
        {
            ResetPullback(state, reason);
            state.MovePhase = MovePhase.Impulse;
            state.MoveAgeBars = 1;
            state.BarsSinceImpulse = 0;
            state.IsStaleImpulse = false;
            state.IsImpulseDecay = false;
            state.HasActiveImpulse = true;
            state.ImpulseDirection = ResolveDirection(bar);
            UpdateImpulseExtremes(state, bar);
        }

        private void IncrementPullback(SymbolMemoryState state, string reason)
        {
            state.PullbackCount = Math.Min(5, state.PullbackCount + 1);
            state.MovePhase = MovePhase.Pullback;
            state.IsImpulseDecay = false;
            _log?.Invoke($"[MEMORY][PULLBACK] symbol={state.Symbol} count={state.PullbackCount} reason={reason}");
        }

        private void ResetPullback(SymbolMemoryState state, string reason)
        {
            state.PullbackCount = 0;
            _log?.Invoke($"[MEMORY][PULLBACK] symbol={state.Symbol} count={state.PullbackCount} reason={reason}");
        }

        private static bool IsEligiblePullback(SymbolMemoryState state, Bar bar)
        {
            return state.HasActiveImpulse && IsRetrace(bar) && !HasContinuation(state, bar);
        }

        private static bool HasContinuation(SymbolMemoryState state, Bar bar)
        {
            if (state == null || bar == null || !state.HasActiveImpulse)
                return false;

            return state.ImpulseDirection switch
            {
                > 0 => bar.High > state.LastImpulseHigh,
                < 0 => bar.Low < state.LastImpulseLow,
                _ => false
            };
        }

        private static bool HasTrendBreak(SymbolMemoryState state, Bar bar)
        {
            if (state == null || bar == null || !state.HasActiveImpulse)
                return false;

            return state.ImpulseDirection switch
            {
                > 0 => bar.Low < state.LastImpulseLow,
                < 0 => bar.High > state.LastImpulseHigh,
                _ => false
            };
        }

        private static void UpdateImpulseExtremes(SymbolMemoryState state, Bar bar)
        {
            if (state == null || bar == null)
                return;

            state.LastImpulseHigh = bar.High;
            state.LastImpulseLow = bar.Low;
        }

        private static int ResolveDirection(Bar bar)
        {
            if (bar == null)
                return 0;

            if (bar.Close > bar.Open)
                return 1;

            if (bar.Close < bar.Open)
                return -1;

            return 0;
        }

        private int CountStates(Func<SymbolMemoryState, bool> predicate)
        {
            int count = 0;

            foreach (var state in States.Values)
            {
                if (state != null && predicate(state))
                    count++;
            }

            return count;
        }

        private (int Total, int Built, int Resolved, int Usable) ComputeCoverage(IEnumerable<string> symbols)
        {
            int totalSymbols = 0;
            int builtSymbols = 0;
            int resolvedSymbols = 0;
            int usableSymbols = 0;

            if (symbols != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var symbol in symbols)
                {
                    if (string.IsNullOrWhiteSpace(symbol))
                        continue;

                    string key = symbol.Trim();
                    if (!seen.Add(key))
                        continue;

                    totalSymbols++;

                    if (!States.TryGetValue(key, out var state) || state == null)
                        continue;

                    if (state.IsBuilt)
                        builtSymbols++;

                    if (state.IsResolved)
                        resolvedSymbols++;

                    if (state.IsUsable)
                        usableSymbols++;
                }
            }

            return (totalSymbols, builtSymbols, resolvedSymbols, usableSymbols);
        }

        private static string FormatCoverage(int covered, int total) => $"{covered}/{total}";

        private static void RecomputeUsable(SymbolMemoryState state)
        {
            if (state == null)
                return;

            state.IsUsable = state.IsBuilt && state.IsResolved;
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

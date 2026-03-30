using System;

namespace GeminiV26.Core.Risk
{
    public class GlobalRiskGuard
    {
        private readonly GeminiRiskConfig _config;
        private readonly Action<string> _log;

        private double _startEquity;
        private DateTime _currentDay;
        private bool _isBlocked;

        public GlobalRiskGuard(GeminiRiskConfig config, Action<string> log)
        {
            _config = config ?? new GeminiRiskConfig();
            _log = log ?? (_ => { });
        }

        public bool CanTrade(double currentEquity, DateTime utcNow)
        {
            DateTime nowUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
            InitializeOrResetDay(currentEquity, nowUtc);

            double baselineEquity = _startEquity > 0 ? _startEquity : Math.Max(currentEquity, 1.0);
            double dailyDrawdownPercent = (baselineEquity - currentEquity) / baselineEquity * 100.0;
            if (dailyDrawdownPercent < 0)
                dailyDrawdownPercent = 0;

            _log($"[RISK][DD] start={baselineEquity:F2} current={currentEquity:F2} dd={dailyDrawdownPercent:F2}%");

            if (dailyDrawdownPercent >= _config.DailyDrawdownLimitPercent)
            {
                if (!_isBlocked)
                {
                    _log($"[RISK][DD_BLOCK] limit={_config.DailyDrawdownLimitPercent:F1}%");
                }

                _isBlocked = true;
                return false;
            }

            _isBlocked = false;
            return true;
        }

        private void InitializeOrResetDay(double currentEquity, DateTime nowUtc)
        {
            DateTime dayUtc = nowUtc.Date;

            if (_currentDay == default)
            {
                _currentDay = dayUtc;
                _startEquity = currentEquity;
                _isBlocked = false;
                _log("[RISK][DD_RESET] new day initialized");
                return;
            }

            if (dayUtc <= _currentDay)
                return;

            _currentDay = dayUtc;
            _startEquity = currentEquity;
            _isBlocked = false;
            _log("[RISK][DD_RESET] new day initialized");
        }
    }
}

using cAlgo.API;

namespace GeminiV26.Core.Matrix
{
    public sealed class SessionMatrix
    {
        private readonly SessionMatrixProvider _provider;

        public SessionMatrix(SessionMatrixProvider provider)
        {
            _provider = provider ?? new SessionMatrixProvider();
        }

        public SessionMatrixConfig Resolve(SessionDecision session, string instrumentClass, TimeFrame tf)
        {
            if (session == null)
                return SessionMatrixDefaults.Neutral;

            var tier = DetectTier(tf);
            return _provider.GetConfig(instrumentClass, session.Bucket, tier);
        }

        public static TimeframeTier DetectTier(TimeFrame tf)
        {
            string tfName = tf.ToString();

            if (tf == TimeFrame.Minute || tfName == "Minute2" || tfName == "Minute3")
                return TimeframeTier.Scalping;

            if (tf == TimeFrame.Minute5)
                return TimeframeTier.Intraday;

            if (tf == TimeFrame.Minute15 || tf == TimeFrame.Minute30 || tf == TimeFrame.Hour)
                return TimeframeTier.BroadIntraday;

            return TimeframeTier.Intraday;
        }
    }
}

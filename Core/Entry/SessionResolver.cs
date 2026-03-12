using GeminiV26.Core;

namespace GeminiV26.Core.Entry
{
    public static class SessionResolver
    {
        public static FxSession FromBucket(SessionBucket bucket)
        {
            switch (bucket)
            {
                case SessionBucket.Asia:
                    return FxSession.Asia;

                case SessionBucket.London:
                case SessionBucket.LondonNyOverlap:
                    return FxSession.London;

                case SessionBucket.NewYork:
                    return FxSession.NewYork;

                default:
                    return FxSession.Off;
            }
        }
    }
}

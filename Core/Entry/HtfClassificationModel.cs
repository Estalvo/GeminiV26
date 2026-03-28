namespace GeminiV26.Core.Entry
{
    public static class HtfClassificationModel
    {
        public static string ComputeHtfClassification(
            TradeDirection candidateDirection,
            TradeDirection htfAllowedDirection)
        {
            if (candidateDirection == TradeDirection.None || htfAllowedDirection == TradeDirection.None)
                return "HTF_NO_DIRECTION";

            if (candidateDirection != htfAllowedDirection)
                return "HTF_MISMATCH";

            return "HTF_OK";
        }
    }
}

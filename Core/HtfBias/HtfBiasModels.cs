namespace GeminiV26.Core.HtfBias
{
    public enum HtfBiasState
    {
        Neutral = 0,
        Bull = 1,
        Bear = 2,
        Transition = 3
    }

    public interface IHtfBiasProvider
    {
        HtfBiasSnapshot Get(string symbolName);
    }
}
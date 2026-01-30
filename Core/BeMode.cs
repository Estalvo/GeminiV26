namespace GeminiV26.Core
{
    public enum BeMode
    {
        None,
        AfterTp1,   // TP1 után azonnal BE+buffer
        Delayed     // késleltetett BE (runner/extra feltétel után)
    }
}

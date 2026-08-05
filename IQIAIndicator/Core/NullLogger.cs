namespace IQIAIndicator.Core;

/// <summary>Logger no-op — silencieux jusqu'à ce qu'un vrai logger soit branché.</summary>
public sealed class NullLogger : ILogger
{
    public static readonly ILogger Instance = new NullLogger();

    private NullLogger() { }

    public void Info(string message)                    { }
    public void Warning(string message)                 { }
    public void Error(string message, Exception? ex = null) { }
}

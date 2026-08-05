namespace IQIAIndicator.Core;

/// <summary>Abstraction de journalisation injectée dans les classes qui en ont besoin.</summary>
public interface ILogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);
}

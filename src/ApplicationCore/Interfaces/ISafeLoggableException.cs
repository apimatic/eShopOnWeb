namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Implemented by exceptions that can expose a log-safe summary containing no personally identifiable
/// information (e.g. no phone number or message text), so the application layer can log a cause
/// without leaking PII.
/// </summary>
public interface ISafeLoggableException
{
    string SafeSummary { get; }
}

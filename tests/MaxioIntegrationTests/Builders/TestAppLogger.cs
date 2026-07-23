using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Captures what the billing client logs, so tests can assert that a degraded path (for example
/// an unreadable usage total) is reported rather than silently swallowed.
/// </summary>
public sealed class TestAppLogger<T> : IAppLogger<T>
{
    public List<string> Informations { get; } = new();

    public List<string> Warnings { get; } = new();

    public void LogInformation(string message, params object[] args) => Informations.Add(Format(message, args));

    public void LogWarning(string message, params object[] args) => Warnings.Add(Format(message, args));

    private static string Format(string message, params object[] args)
    {
        try
        {
            return args.Length == 0 ? message : string.Format(message, args);
        }
        catch (FormatException)
        {
            return message;
        }
    }
}

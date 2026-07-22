using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Captures what the integration logged, so tests can assert that a degraded path was reported
/// rather than swallowed silently.
/// </summary>
internal sealed class RecordingLogger<T> : IAppLogger<T>
{
    internal List<string> Informations { get; } = new();

    internal List<string> Warnings { get; } = new();

    public void LogInformation(string message, params object[] args) => Informations.Add(Format(message, args));

    public void LogWarning(string message, params object[] args) => Warnings.Add(Format(message, args));

    private static string Format(string message, object[] args)
    {
        try
        {
            return args.Length == 0 ? message : string.Format(message, args);
        }
        catch (FormatException)
        {
            // A message whose placeholders do not line up is still worth recording verbatim.
            return message;
        }
    }
}

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Captures what the integration logged, so "logged and swallowed" paths can be asserted.</summary>
internal sealed class RecordingAppLogger<T> : IAppLogger<T>
{
    public List<string> Information { get; } = new();

    public List<string> Warnings { get; } = new();

    public void LogInformation(string message, params object[] args) => Information.Add(message);

    public void LogWarning(string message, params object[] args) => Warnings.Add(message);
}

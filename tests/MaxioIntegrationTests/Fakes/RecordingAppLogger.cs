using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Captures what the integration logs, so tests can assert secrets never reach it.</summary>
internal sealed class RecordingAppLogger<T> : IAppLogger<T>
{
    public List<string> Messages { get; } = new();

    public void LogInformation(string message, params object[] args) => Messages.Add(message);

    public void LogWarning(string message, params object[] args) => Messages.Add(message);
}

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Captures what the integration logs, so leaks into the log can be asserted against.</summary>
public sealed class TestLogger<T> : IAppLogger<T>
{
    public List<string> Messages { get; } = new();

    public void LogInformation(string message, params object[] args) => Record(message, args);

    public void LogWarning(string message, params object[] args) => Record(message, args);

    /// <summary>Everything written, template and arguments alike, as one searchable string.</summary>
    public string AllText => string.Join("\n", Messages);

    private void Record(string message, object[] args) =>
        Messages.Add(message + " " + string.Join(" ", args.Select(argument => argument?.ToString() ?? string.Empty)));
}

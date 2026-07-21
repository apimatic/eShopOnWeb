using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Support;

/// <summary>Records log calls for assertions instead of writing anywhere.</summary>
public class FakeAppLogger<T> : IAppLogger<T>
{
    public List<string> Warnings { get; } = new();
    public List<string> Information { get; } = new();

    public void LogWarning(string message, params object[] args) => Warnings.Add(message);

    public void LogInformation(string message, params object[] args) => Information.Add(message);
}

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;

public class FakeAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }
}

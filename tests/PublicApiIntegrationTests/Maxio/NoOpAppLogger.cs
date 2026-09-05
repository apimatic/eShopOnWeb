using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.Maxio;

public class NoOpAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }
}

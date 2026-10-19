using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using PublicApiIntegrationTests.SubscriptionEndpoints;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateFactory();

    public static HttpClient NewClient
    {
        get
        {
            return _application.CreateClient();
        }
    }

    // Placeholder (non-secret) Maxio settings so the host's startup validation passes without real credentials.
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(MaxioTestSettings.Apply);

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = CreateFactory();

    }
}

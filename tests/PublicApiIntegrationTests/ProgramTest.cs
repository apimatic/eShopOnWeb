using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new();

    public static HttpClient NewClient
    {
        get
        {
            return _application.CreateClient();
        }
    }

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        // The sandbox credentials live in the environment; the app reads them from the "Maxio:" section.
        Bridge("MAXIO_API_KEY", "Maxio__ApiKey");
        Bridge("MAXIO_SITE_SUBDOMAIN", "Maxio__Subdomain");
        Bridge("MAXIO_ENVIRONMENT", "Maxio__Environment");
        _application = new WebApplicationFactory<Program>();
    }

    private static void Bridge(string from, string to)
    {
        var value = System.Environment.GetEnvironmentVariable(from);
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(to)))
            System.Environment.SetEnvironmentVariable(to, value);
    }
}

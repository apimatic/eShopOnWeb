using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;

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
        // Test hosts must never resolve real billing credentials or contact a sandbox.
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Maxio:ApiKey", "test-only")
            .UseSetting("Maxio:Subdomain", "test-only")
            .UseSetting("Maxio:ProductFamilyHandle", "test-family")
            .UseSetting("Maxio:BaseUrl", "https://billing.example.test"));

    }
}

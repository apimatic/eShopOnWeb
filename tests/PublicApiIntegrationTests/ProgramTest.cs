using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        _application = CreateFactory();
    }

    /// <summary>
    /// Placeholder (non-secret) billing settings so the host passes its startup validation without
    /// real credentials; BaseUrl points at an unroutable host so no test can reach a real billing site.
    /// </summary>
    public static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                // Read while services are registered, so it must be a host setting; this host has no LocalDB.
                .UseSetting("UseOnlyInMemoryDatabase", "true")
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "integration-test-placeholder",
                ["Maxio:BaseUrl"] = "https://maxio.invalid",
                ["Maxio:ProductFamilyHandle"] = "test-family"
            })));
}

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
    /// <summary>
    /// Placeholder (non-secret) Maxio settings so the host passes its startup validation without user-secrets.
    /// BaseUrl points at a reserved, unresolvable host: these tests can never reach a real Maxio site.
    /// </summary>
    public static readonly Dictionary<string, string?> TestMaxioSettings = new()
    {
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = "test-site",
        ["Maxio:ProductFamilyHandle"] = "test-family",
        ["Maxio:BaseUrl"] = "https://maxio.test.invalid",
        ["Maxio:PlanCacheSeconds"] = "0"
    };

    private static WebApplicationFactory<Program> _application = CreateFactory();

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

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestMaxioSettings)));
}

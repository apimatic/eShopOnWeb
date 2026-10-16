using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateApplication();

    private static WebApplicationFactory<Program> CreateApplication() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-only-key", ["Maxio:Subdomain"] = "test-only-site",
                ["Maxio:ProductFamilyHandle"] = "test-only-family", ["Maxio:BaseUrl"] = "https://billing.invalid"
            })));

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
        _application = CreateApplication();

    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Net.Http;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    /// <summary>
    /// Non-secret placeholder Maxio settings so the host passes its startup validation on a machine without
    /// user-secrets. Maxio:BaseUrl points at a closed local port: these tests never reach a real Maxio site.
    /// </summary>
    public static readonly Dictionary<string, string?> MaxioPlaceholderSettings = new()
    {
        ["Maxio:ApiKey"] = "test-placeholder-not-a-key",
        ["Maxio:Subdomain"] = "test-placeholder",
        ["Maxio:ProductFamilyHandle"] = "test-placeholder-family",
        ["Maxio:BaseUrl"] = "http://127.0.0.1:9",
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
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(MaxioPlaceholderSettings)));
}

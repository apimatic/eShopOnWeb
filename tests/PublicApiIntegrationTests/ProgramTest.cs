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
    private static WebApplicationFactory<Program> _application = CreateApplication();

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

    private static WebApplicationFactory<Program> CreateApplication() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Non-secret placeholder PayPal config so the startup credential check passes under test.
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PayPal:ClientId"] = "test-client-id",
                    ["PayPal:ClientSecret"] = "test-client-secret",
                    ["PayPal:Environment"] = "sandbox",
                    ["PayPal:Currency"] = "USD",
                });
            });
        });
}

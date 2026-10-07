using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http;

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

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = CreateFactory();
    }

    /// <summary>Fake Adyen settings: the tests never use real credentials or reach the network.</summary>
    public static Dictionary<string, string?> FakeAdyenSettings() => new()
    {
        ["Adyen:ApiKey"] = "integration-test-key",
        ["Adyen:MerchantAccount"] = "IntegrationTestMerchant",
        ["Adyen:Environment"] = "test",
        ["Adyen:Currency"] = "USD",
    };

    public static WebApplicationFactory<Program> CreateFactory(Dictionary<string, string?>? adyenSettings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(adyenSettings ?? FakeAdyenSettings()));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(AdyenServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(AdyenStub.CreateHandler));
        });
}

using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new();

    public static HttpClient NewClient => _application.CreateClient();

    public static TestMaxioBillingGateway BillingGateway =>
        _application.Services.GetRequiredService<TestMaxioBillingGateway>();

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = "integration-test-key",
                    ["Maxio:Subdomain"] = "integration-test-site",
                    ["Maxio:ProductFamilyHandle"] = "integration-test-family"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMaxioBillingGateway>();
                services.AddSingleton<TestMaxioBillingGateway>();
                services.AddSingleton<IMaxioBillingGateway>(provider =>
                    provider.GetRequiredService<TestMaxioBillingGateway>());
            });
        });
    }
}

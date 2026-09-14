using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = new();

    public static HttpClient NewClient => _application.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public static WebApplicationFactory<Program> Application => _application;

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = new string('x', 16),
                    ["Maxio:Subdomain"] = "test-site",
                    ["Maxio:ProductFamilyHandle"] = FakeMaxioClient.ProductFamilyHandle,
                    ["Maxio:BaseUrl"] = "https://maxio.test"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaxioClient>();
                services.AddSingleton<FakeMaxioClient>();
                services.AddSingleton<IMaxioClient>(provider => provider.GetRequiredService<FakeMaxioClient>());
            });
        });
    }
}

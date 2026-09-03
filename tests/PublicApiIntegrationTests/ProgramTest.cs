using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

[TestClass]
public class ProgramTest
{
    private static WebApplicationFactory<Program> _application = CreateFactory();

    public static HttpClient NewClient => _application.CreateClient();

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _application = CreateFactory();
    }

    public static HttpClient CreateClient(ISubscriptionBillingService billingService)
    {
        return CreateFactory(services =>
        {
            services.RemoveAll<ISubscriptionBillingService>();
            services.AddSingleton(billingService);
        }).CreateClient();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = "integration-test-api-key",
                    ["Maxio:Subdomain"] = "integration-test-site",
                    ["Maxio:ProductFamilyHandle"] = "integration-test-family"
                });
            });

            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });
    }
}

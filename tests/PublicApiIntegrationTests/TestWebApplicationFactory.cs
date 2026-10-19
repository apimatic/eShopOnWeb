using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests;

/// <summary>
/// Boots PublicApi on the in-memory database with placeholder Maxio settings, so tests never pick up
/// real credentials (user-secrets / MAXIO_* variables) and never reach a real Maxio site.
/// Pass a handler to answer the Maxio client's HTTP calls in-process.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly HttpMessageHandler? _maxioHandler;

    public TestWebApplicationFactory(HttpMessageHandler? maxioHandler = null)
    {
        _maxioHandler = maxioHandler;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family",
            ["Maxio:BaseUrl"] = "https://maxio.invalid",
            ["Maxio:DefaultPlanHandle"] = "pro"
        }));

        if (_maxioHandler is not null)
        {
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => _maxioHandler));
        }
    }
}

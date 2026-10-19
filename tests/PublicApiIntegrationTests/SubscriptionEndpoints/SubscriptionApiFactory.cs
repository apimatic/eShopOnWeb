using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// PublicApi host with the Maxio HttpClient routed to a <see cref="MaxioStub"/>. Uses the "Testing"
/// environment so no developer user-secrets are loaded, and dummy, non-secret Maxio settings.
/// </summary>
public sealed class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    private readonly MaxioStub _stub;
    private readonly Guid _databaseId = Guid.NewGuid();
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["UseOnlyInMemoryDatabase"] = "true",
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = "test-site",
        ["Maxio:ProductFamilyHandle"] = "test-family",
        ["Maxio:BaseUrl"] = "https://maxio.test.invalid",
    };

    public SubscriptionApiFactory(MaxioStub stub, IDictionary<string, string?>? overrides = null)
    {
        _stub = stub;
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
        {
            // Each factory gets its own catalog store, so billing claims never leak between tests.
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase($"Catalog-{_databaseId}"));

            services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _stub);
        });
    }

    public HttpClient ClientFor(string token)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

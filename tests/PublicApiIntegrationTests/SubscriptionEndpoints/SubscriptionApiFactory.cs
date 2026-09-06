using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Boots the PublicApi with the Maxio transport replaced by <see cref="FakeMaxioApiClient"/>,
/// so the endpoints, authentication, mapping and idempotency logic are exercised end to end
/// without touching the sandbox. The Maxio settings here are placeholders - never real
/// credentials, which live in user-secrets or the environment.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public SubscriptionApiFactory(bool configureMaxio = true, string? defaultPlanHandle = null)
    {
        ConfigureMaxio = configureMaxio;
        DefaultPlanHandle = defaultPlanHandle;
    }

    public FakeMaxioApiClient Maxio { get; } = new();

    private bool ConfigureMaxio { get; }

    private string? DefaultPlanHandle { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["Maxio:ApiKey"] = ConfigureMaxio ? "test-api-key" : string.Empty,
                ["Maxio:Subdomain"] = ConfigureMaxio ? "test-site" : string.Empty,
                ["Maxio:ProductFamilyHandle"] = ConfigureMaxio ? "eshop-subscribe" : string.Empty,
                ["Maxio:BaseUrl"] = string.Empty,
                ["Maxio:Environment"] = "US",
                // Pinned so the tests never depend on whatever a developer keeps in user-secrets.
                ["Maxio:DefaultPlanHandle"] = DefaultPlanHandle ?? string.Empty,
                ["Maxio:PaymentCollectionMethod"] = "remittance",
                ["Maxio:PlanCacheSeconds"] = "0"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMaxioApiClient>();
            services.AddSingleton<IMaxioApiClient>(Maxio);
        });
    }

    public HttpClient CreateAuthenticatedClient(string? token = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token ?? ApiTokenHelper.GetNormalUserToken());

        return client;
    }
}

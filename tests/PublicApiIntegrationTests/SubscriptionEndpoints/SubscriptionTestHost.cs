using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// A PublicApi test host with the Maxio HTTP client replaced by an in-memory fake so the
/// subscription endpoints can be exercised without any network access.
/// </summary>
internal sealed class SubscriptionTestHost : IDisposable
{
    private SubscriptionTestHost(WebApplicationFactory<Program> factory, FakeMaxioApiClient maxio)
    {
        Factory = factory;
        Maxio = maxio;
    }

    public WebApplicationFactory<Program> Factory { get; }

    public FakeMaxioApiClient Maxio { get; }

    public static SubscriptionTestHost Create()
    {
        var maxio = new FakeMaxioApiClient();

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Maxio:ApiKey"] = "unit-test-api-key",
                        ["Maxio:Subdomain"] = "unit-test-subdomain",
                        ["Maxio:Environment"] = "US",
                        ["Maxio:ProductFamilyHandle"] = "eshop-subscribe"
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMaxioApiClient>();
                    services.AddSingleton<IMaxioApiClient>(maxio);

                    // The EF in-memory store is process-wide and keyed by database name, and it is
                    // shared across WebApplicationFactory hosts in the same test process. Give each
                    // host its own mapping database so tests cannot observe one another's state.
                    services.RemoveAll<DbContextOptions<SubscriptionMappingDbContext>>();
                    services.AddDbContext<SubscriptionMappingDbContext>(options =>
                        options.UseInMemoryDatabase($"SubscriptionMapping-{Guid.NewGuid():N}"));
                });
            });

        return new SubscriptionTestHost(factory, maxio);
    }

    public void Dispose()
    {
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}


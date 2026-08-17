using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SmsNotificationEndpoints;

/// <summary>
/// A PublicApi test host with the Twilio provider replaced by <see cref="FakeMessagingProvider"/> and its
/// in-memory stores given unique names, so each factory instance is fully isolated (EF's in-memory store is
/// otherwise process-global and would leak data between tests). The flows run end-to-end through the real
/// endpoints/services with no real network traffic.
/// </summary>
public sealed class SmsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _suffix = Guid.NewGuid().ToString("N");

    public FakeMessagingProvider Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            IsolateDatabase<CatalogContext>(services, $"Catalog-{_suffix}");
            IsolateDatabase<AppIdentityDbContext>(services, $"Identity-{_suffix}");

            var provider = services.SingleOrDefault(d => d.ServiceType == typeof(IMessagingProvider));
            if (provider is not null)
            {
                services.Remove(provider);
            }
            services.AddSingleton<IMessagingProvider>(Provider);
        });
    }

    private static void IsolateDatabase<TContext>(IServiceCollection services, string databaseName)
        where TContext : DbContext
    {
        foreach (var descriptor in services
                     .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>) || d.ServiceType == typeof(TContext))
                     .ToList())
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(databaseName));
    }
}

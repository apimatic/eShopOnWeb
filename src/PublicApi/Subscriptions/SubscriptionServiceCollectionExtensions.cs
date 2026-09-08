using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Registers the Maxio subscription billing feature (settings, HTTP client, local mapping store and
/// orchestration service). Settings are bound from the <c>Maxio</c> configuration section and read from
/// .NET user-secrets at runtime — no secrets are compiled or checked into the repository.
/// </summary>
public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.ConfigurationSectionName));

        services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        services.AddDbContext<SubscriptionMappingDbContext>(options =>
        {
            if (useOnlyInMemoryDatabase)
            {
                options.UseInMemoryDatabase("SubscriptionMapping");
            }
            else
            {
                options.UseSqlServer(configuration.GetConnectionString("SubscriptionConnection"));
            }
        });

        return services;
    }

    /// <summary>
    /// Creates the schema for the subscription mapping store when running on a real database.
    /// The in-memory provider (used for local development / tests) creates its schema automatically.
    /// </summary>
    public static async Task EnsureSubscriptionDatabaseCreatedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionMappingDbContext>();

        if (db.Database.IsSqlServer())
        {
            await db.Database.EnsureCreatedAsync();
        }
    }
}

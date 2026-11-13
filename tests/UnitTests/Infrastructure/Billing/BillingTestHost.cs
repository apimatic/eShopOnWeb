using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>A clock the test moves by hand. Timers fall back to real time (only SDK retry backoff uses them).</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Builds the real billing registration (<see cref="MaxioServiceCollectionExtensions.AddMaxioSubscriptionBilling"/>)
/// over an in-memory CatalogContext, with <see cref="FakeMaxio"/> as the SDK HttpClient's primary handler.
/// </summary>
public sealed class BillingTestHost : IDisposable
{
    public const string FamilyHandle = "test-family";

    public BillingTestHost(IDictionary<string, string?>? overrides = null)
    {
        Maxio = new FakeMaxio(FamilyHandle)
            .WithProduct("pro", "Pro", 29900)
            .WithProduct("basic", "Basic", 2900)
            .WithProduct("legacy", "Legacy", 100, archived: true);

        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "unit-test-placeholder-key",
            ["Maxio:Subdomain"] = "unit-test-site",
            ["Maxio:ProductFamilyHandle"] = FamilyHandle
        };
        foreach (var pair in overrides ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var databaseName = "billing-" + Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Maxio);
        Provider = services.BuildServiceProvider();
    }

    public FakeMaxio Maxio { get; }

    public ManualTimeProvider Clock { get; } = new();

    public ServiceProvider Provider { get; }

    public static BillingCustomerProfile Profile(string userName = "demouser@microsoft.com")
        => new(userName, userName, userName.Split('@')[0], "eShopOnWeb customer");

    /// <summary>Runs <paramref name="action"/> in its own DI scope (its own DbContext), like one HTTP request.</summary>
    public async Task<T> InRequest<T>(Func<ISubscriptionBillingService, Task<T>> action)
    {
        using var scope = Provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>());
    }

    public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public void Dispose() => Provider.Dispose();
}

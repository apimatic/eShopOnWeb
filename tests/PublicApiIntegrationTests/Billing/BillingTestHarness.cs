using System;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PublicApiIntegrationTests.Billing;

/// <summary>
/// A <see cref="SubscriptionBillingService"/> wired to <see cref="FakeMaxio"/> and an isolated EF in-memory store.
/// Several services can share one store, which is how concurrent callers are simulated.
/// </summary>
public sealed class BillingTestHarness
{
    private readonly string _databaseName = "billing-" + Guid.NewGuid().ToString("N");
    // One root for every harness: a new root per test would make EF build a new internal service provider each time.
    private static readonly InMemoryDatabaseRoot _root = new();

    public BillingTestHarness(FakeMaxio? maxio = null)
    {
        Maxio = maxio ?? FakeMaxio.WithDefaultPlans();
        Client = Maxio.CreateClient();
    }

    public FakeMaxio Maxio { get; }
    public MaxioAdvancedBillingClient Client { get; }

    /// <summary>Shared by every service of this harness, as the singleton is shared by every request in the app.</summary>
    public SiteCollectionMethodCache SiteCache { get; } = new();

    public static BillingCaller Caller(string userName = "shopper@example.test") => new(userName, userName);

    public CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName, _root).Options);

    /// <summary>A service with its own DbContext, as one HTTP request scope would have.</summary>
    public SubscriptionBillingService NewService() =>
        new(Client, SiteCache, NewContext(), Options.Create(Maxio.Settings()), TimeProvider.System, NullLogger<SubscriptionBillingService>.Instance);

    public static Exception ConnectionReset() => new HttpRequestException("connection reset (simulated)");

    public static async Task<T> WithTimeout<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(30));
}

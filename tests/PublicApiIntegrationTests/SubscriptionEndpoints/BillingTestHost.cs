using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Builds the real billing service — real SDK client with the production client options, real EF claim store —
/// over a <see cref="FakeMaxio"/> and an isolated in-memory database. Each call to <see cref="CreateService"/>
/// is a separate "request" (own DbContext) sharing the same database, the way two concurrent requests would.
/// </summary>
public sealed class BillingTestHost : IDisposable
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public BillingTestHost()
    {
        var settings = Settings();
        Client = new MaxioAdvancedBillingClient(
            new HttpClient(Maxio),
            BillingServiceCollectionExtensions.CreateClientOptions(settings, NullLoggerFactory.Instance));
    }

    public FakeMaxio Maxio { get; } = new();

    public MutableTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public MaxioAdvancedBillingClient Client { get; }

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-placeholder-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FakeMaxio.FamilyHandle,
        BaseUrl = FakeMaxio.BaseUrl
    };

    public CatalogContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(_databaseName, _databaseRoot)
            .Options);

    public MaxioSubscriptionBillingService CreateService(bool sharedCache = false) =>
        new(Client,
            new EfBillingClaimStore(CreateDbContext()),
            Options.Create(Settings()),
            sharedCache ? _cache : new MemoryCache(new MemoryCacheOptions()),
            Clock,
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    public void Dispose() => _cache.Dispose();
}

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;

/// <summary>
/// Builds <see cref="MaxioSubscriptionBillingService"/> instances the way the host does, over a
/// <see cref="FakeMaxio"/> handler and a shared in-memory claim store. Each <see cref="NewService"/>
/// call is one "request scope" with its own DbContext.
/// </summary>
public sealed class BillingHarness
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly MaxioAdvancedBillingClient _client;

    public BillingHarness(FakeMaxio? maxio = null, Action<MaxioSettings>? configure = null)
    {
        Maxio = maxio ?? FakeMaxio.WithDefaultCatalog();
        Settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            BaseUrl = "https://maxio.test",
            AttemptTimeoutSeconds = 5,
            MaxReadRetries = 0,
            SettlementBudgetSeconds = 5,
            OperationBudgetSeconds = 20,
        };
        configure?.Invoke(Settings);

        var options = MaxioServiceCollectionExtensions.CreateClientOptions(Settings, NullLoggerFactory.Instance, TimeProvider.System);
        _client = new MaxioAdvancedBillingClient(new HttpClient(Maxio), options);
    }

    public FakeMaxio Maxio { get; }

    public MaxioSettings Settings { get; }

    public ManualTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-10-15T10:00:00Z"));

    public MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    public CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_databaseName, Root).Options);

    public SubscriptionClaimStore NewStore() => new(NewContext());

    public MaxioSubscriptionBillingService NewService() =>
        new(_client, NewStore(), Cache, Options.Create(Settings), Clock, NullLogger<MaxioSubscriptionBillingService>.Instance);
}

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The billing services wired exactly as production registers them, with the in-memory EF provider and the
/// SDK's HttpClient pointed at <see cref="FakeMaxio"/>. Settings are fake, non-secret test values.
/// </summary>
public sealed class BillingTestHost : IAsyncDisposable
{
    public const string UserName = "shopper@test.local";

    public static readonly Dictionary<string, string?> FakeMaxioSettings = new()
    {
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = "test-site",
        ["Maxio:ProductFamilyHandle"] = FakeMaxio.FamilyHandle,
        ["Maxio:BaseUrl"] = "https://maxio.test.invalid",
    };

    private BillingTestHost(FakeMaxio maxio, MutableClock clock, ServiceProvider services)
    {
        Maxio = maxio;
        Clock = clock;
        Services = services;
    }

    public MutableClock Clock { get; }

    public FakeMaxio Maxio { get; }

    public ServiceProvider Services { get; }

    public string UserId { get; private set; } = string.Empty;

    public static async Task<BillingTestHost> CreateAsync()
    {
        var maxio = new FakeMaxio();
        var clock = new MutableClock();
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase($"billing-{databaseName}"));
        services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase($"identity-{databaseName}"));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppIdentityDbContext>();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(FakeMaxioSettings).Build();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient("MaxioAdvancedBilling").ConfigurePrimaryHttpMessageHandler(() => maxio);

        var host = new BillingTestHost(maxio, clock, services.BuildServiceProvider());
        using var scope = host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = UserName, Email = UserName };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        }
        host.UserId = user.Id;
        return host;
    }

    /// <summary>Runs <paramref name="work"/> in its own DI scope (its own DbContext), like one HTTP request.</summary>
    public async Task<T> InRequestAsync<T>(Func<ISubscriptionBillingService, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ISubscriptionBillingService>());
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task<SubscriptionEnrollment?> GetEnrollmentAsync() => InScopeAsync(sp =>
        sp.GetRequiredService<CatalogContext>().SubscriptionEnrollments.AsNoTracking().SingleOrDefaultAsync());

    public Task<BillingCustomer?> GetBillingCustomerAsync() => InScopeAsync(sp =>
        sp.GetRequiredService<CatalogContext>().BillingCustomers.AsNoTracking().SingleOrDefaultAsync());

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}

/// <summary>A clock the test can move forward; only <see cref="TimeProvider.GetUtcNow"/> is used by the billing service.</summary>
public sealed class MutableClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

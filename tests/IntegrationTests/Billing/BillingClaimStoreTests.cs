using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Pins the store behaviour the billing claims depend on: the in-memory provider (used when
/// UseOnlyInMemoryDatabase=true) must refuse a second claim with the same key, and a stale concurrency token.
/// </summary>
public class BillingClaimStoreTests
{
    private readonly DbContextOptions<CatalogContext> _options = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase("claims-" + Guid.NewGuid())
        .Options;

    [Fact]
    public async Task SecondClaimWithSameKey_IsRefusedByTheStore()
    {
        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);

        first.SubscriptionClaims.Add(new SubscriptionClaim("user-1", "pro", DateTimeOffset.UtcNow));
        await first.SaveChangesAsync();

        second.SubscriptionClaims.Add(new SubscriptionClaim("user-1", "pro", DateTimeOffset.UtcNow));
        var ex = await Record.ExceptionAsync(() => second.SaveChangesAsync());

        // MaxioSubscriptionBillingService.TrySaveClaimAsync catches exactly these types.
        Assert.True(ex is DbUpdateException or InvalidOperationException or ArgumentException, ex?.GetType().FullName);
    }

    [Fact]
    public async Task RetakingAClaimWithAStaleToken_IsRefusedByTheStore()
    {
        await using (var seed = new CatalogContext(_options))
        {
            seed.BillingCustomerLinks.Add(new BillingCustomerLink("user-1", DateTimeOffset.UtcNow.AddHours(-1)));
            await seed.SaveChangesAsync();
        }

        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);
        var a = await first.BillingCustomerLinks.SingleAsync();
        var b = await second.BillingCustomerLinks.SingleAsync();

        a.Retake(DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();

        b.Retake(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}

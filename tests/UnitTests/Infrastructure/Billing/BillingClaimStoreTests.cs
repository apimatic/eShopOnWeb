using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class BillingClaimStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly DbContextOptions<CatalogContext> _options =
        new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase("claims-" + Guid.NewGuid()).Options;

    [Fact]
    public async Task TheStoreRefusesASecondClaimForTheSameReference()
    {
        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);

        var a = await new BillingClaimStore(first).TryClaimAsync(new BillingCustomerLink("ref-1", "u", Now, Lease), Now, Lease, default);
        var b = await new BillingClaimStore(second).TryClaimAsync(new BillingCustomerLink("ref-1", "u", Now, Lease), Now, Lease, default);

        Assert.True(a.Acquired);
        Assert.False(b.Acquired);
        Assert.Equal(BillingClaimStatus.Pending, b.Claim.Status);
    }

    [Fact]
    public async Task TheInMemoryProviderRaisesArgumentExceptionOnADuplicateKey()
    {
        // Pins the exception BillingClaimStore.IsDuplicateKey relies on for the in-memory provider.
        await using (var first = new CatalogContext(_options))
        {
            first.BillingCustomerLinks.Add(new BillingCustomerLink("ref-dup", "u", Now, Lease));
            await first.SaveChangesAsync();
        }

        await using var second = new CatalogContext(_options);
        second.BillingCustomerLinks.Add(new BillingCustomerLink("ref-dup", "u", Now, Lease));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task AnAbandonedClaimCanBeTakenOverByExactlyOneRequest()
    {
        await using (var holder = new CatalogContext(_options))
        {
            await new BillingClaimStore(holder).TryClaimAsync(new SubscriptionEnrollment("sub-1", "cus-1", "pro", Now, Lease), Now, Lease, default);
        }

        var later = Now + Lease + TimeSpan.FromSeconds(1);
        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);
        var storeA = new BillingClaimStore(first);
        var storeB = new BillingClaimStore(second);

        // Both read the expired claim before either renews it.
        var claimA = await first.SubscriptionEnrollments.SingleAsync();
        var claimB = await second.SubscriptionEnrollments.SingleAsync();
        var a = await storeA.TryRenewAsync(claimA, later, Lease, default);
        var b = await storeB.TryRenewAsync(claimB, later, Lease, default);

        Assert.True(a);
        Assert.False(b);
    }

    [Fact]
    public async Task ACompletedClaimIsReturnedToLaterCallers()
    {
        await using (var ctx = new CatalogContext(_options))
        {
            var store = new BillingClaimStore(ctx);
            var claim = (await store.TryClaimAsync(new BillingCustomerLink("ref-2", "u", Now, Lease), Now, Lease, default)).Claim;
            Assert.True(await store.TryCompleteAsync(claim, 42, Now, default));
        }

        await using var other = new CatalogContext(_options);
        var later = Now + TimeSpan.FromDays(1);
        var attempt = await new BillingClaimStore(other).TryClaimAsync(new BillingCustomerLink("ref-2", "u", later, Lease), later, Lease, default);

        Assert.False(attempt.Acquired);
        Assert.Equal(BillingClaimStatus.Completed, attempt.Claim.Status);
        Assert.Equal(42, attempt.Claim.ProviderId);
    }

    [Fact]
    public async Task AReleasedClaimCanBeClaimedAgain()
    {
        await using (var ctx = new CatalogContext(_options))
        {
            var store = new BillingClaimStore(ctx);
            var claim = (await store.TryClaimAsync(new BillingCustomerLink("ref-3", "u", Now, Lease), Now, Lease, default)).Claim;
            await store.ReleaseAsync(claim, default);
        }

        await using var other = new CatalogContext(_options);
        var attempt = await new BillingClaimStore(other).TryClaimAsync(new BillingCustomerLink("ref-3", "u", Now, Lease), Now, Lease, default);

        Assert.True(attempt.Acquired);
    }
}

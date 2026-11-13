using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Proves, against the EF InMemory provider this environment runs on, that the store itself refuses a second
/// claim (primary key) and a stale take-over (concurrency token).
/// </summary>
[TestClass]
public class BillingClaimStoreTest
{
    private static CatalogContext NewContext(string database) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(database).Options);

    [TestMethod]
    public async Task SecondClaimForTheSameKeyIsRefused()
    {
        var database = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        await using var first = NewContext(database);
        await using var second = NewContext(database);

        Assert.IsTrue(await new EfBillingClaimStore(first).TryInsertAsync(new BillingClaim("eshop-sub-1", BillingClaimKind.Subscription, "eshop-pro", now)));
        Assert.IsFalse(await new EfBillingClaimStore(second).TryInsertAsync(new BillingClaim("eshop-sub-1", BillingClaimKind.Subscription, "eshop-pro", now)));
    }

    [TestMethod]
    public async Task InMemoryProviderRefusesADuplicatePrimaryKeyWithArgumentException()
    {
        // EfBillingClaimStore.IsDuplicateKeyCandidate relies on this exact exception type for the InMemory provider.
        var database = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        await using var first = NewContext(database);
        await using var second = NewContext(database);
        first.BillingClaims.Add(new BillingClaim("k", BillingClaimKind.Customer, null, now));
        await first.SaveChangesAsync();
        second.BillingClaims.Add(new BillingClaim("k", BillingClaimKind.Customer, null, now));

        await Assert.ThrowsExceptionAsync<ArgumentException>(() => second.SaveChangesAsync());
    }

    [TestMethod]
    public async Task OnlyOneCallerCanTakeOverAClaim()
    {
        var database = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = NewContext(database))
        {
            var claim = new BillingClaim("eshop-cus-1", BillingClaimKind.Customer, null, now);
            var store = new EfBillingClaimStore(setup);
            await store.TryInsertAsync(claim);
            await store.TryUpdateAsync(claim, c => c.MarkUnknown(now));
        }

        await using var a = NewContext(database);
        await using var b = NewContext(database);
        var storeA = new EfBillingClaimStore(a);
        var storeB = new EfBillingClaimStore(b);
        var seenByA = await storeA.FindAsync("eshop-cus-1");
        var seenByB = await storeB.FindAsync("eshop-cus-1");

        Assert.IsTrue(await storeA.TryUpdateAsync(seenByA!, c => c.Begin(null, now)));
        Assert.IsFalse(await storeB.TryUpdateAsync(seenByB!, c => c.Begin(null, now)), "the second take-over must be refused");
        Assert.IsFalse(await storeB.TryReleaseAsync(seenByB!), "a release based on a stale read must be refused");
        Assert.AreEqual(BillingClaimState.Pending, (await storeA.FindAsync("eshop-cus-1"))!.State);
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

/// <summary>
/// Pins what the store this app runs on (EF Core in-memory) does with a second claim, because the
/// duplicate-claim guard depends on it: the second insert of the same key, and a takeover racing
/// another, must both be refused by the store itself.
/// </summary>
[TestClass]
public class BillingClaimStoreTests
{
    [TestMethod]
    public async Task SecondInsertOfTheSameClaimKey_IsRefusedByTheStore()
    {
        var harness = new BillingTestHarness();
        var now = DateTimeOffset.UtcNow;
        using (var first = harness.NewContext())
        {
            first.SubscriptionEnrollments.Add(new SubscriptionEnrollment("eshop-ref", "pro-test", now));
            await first.SaveChangesAsync();
        }

        using var second = harness.NewContext();
        second.SubscriptionEnrollments.Add(new SubscriptionEnrollment("eshop-ref", "pro-test", now));

        // EF Core 8 in-memory raises ArgumentException ("An item with the same key has already been added");
        // SQL Server raises DbUpdateException. SubscriptionBillingService.ClaimAsync catches both and then
        // confirms the key is taken before treating it as a duplicate claim.
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => second.SaveChangesAsync());
    }

    [TestMethod]
    public async Task ConcurrentTakeoverOfTheSameClaim_IsRefusedByTheConcurrencyToken()
    {
        var harness = new BillingTestHarness();
        var now = DateTimeOffset.UtcNow;
        using (var seed = harness.NewContext())
        {
            var link = new BillingCustomerLink("eshop-ref", now);
            link.MarkUnknown(now);
            seed.BillingCustomerLinks.Add(link);
            await seed.SaveChangesAsync();
        }

        using var a = harness.NewContext();
        using var b = harness.NewContext();
        var linkA = await a.BillingCustomerLinks.SingleAsync();
        var linkB = await b.BillingCustomerLinks.SingleAsync();

        linkA.TakeOver(now);
        await a.SaveChangesAsync();

        linkB.TakeOver(now);
        await Assert.ThrowsExceptionAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
}

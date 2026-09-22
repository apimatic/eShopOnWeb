using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Verifies the duplicate-write guards hold under the in-memory provider (the mode this app runs in):
/// OrderPayment's primary key (OrderId) and OrderRefund's alternate key (OrderId, IdempotencyKey) both
/// reject a second row, which is what makes the pay/refund idempotency claims real.
/// </summary>
public class PaymentIdempotencyClaimTests
{
    private static CatalogContext ContextFor(string dbName) =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public async Task DuplicateOrderPayment_ForSameOrderId_IsRejected()
    {
        var db = "payment-" + Guid.NewGuid();

        using (var ctx = ContextFor(db))
        {
            ctx.OrderPayments.Add(new OrderPayment(1, "buyer@example.com", "USD", 10m, "INV-1", null));
            await ctx.SaveChangesAsync();
        }

        using (var ctx = ContextFor(db))
        {
            ctx.OrderPayments.Add(new OrderPayment(1, "buyer@example.com", "USD", 10m, "INV-2", null));
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Refund_SameOrderAndKey_Rejected_ButDistinctKeysAllowed()
    {
        var db = "refund-" + Guid.NewGuid();

        using (var ctx = ContextFor(db))
        {
            ctx.OrderRefunds.Add(new OrderRefund(5, "key-A", 3m, "USD"));
            await ctx.SaveChangesAsync();
        }

        // A distinct key for the same order is a legitimate second (partial) refund.
        using (var ctx = ContextFor(db))
        {
            ctx.OrderRefunds.Add(new OrderRefund(5, "key-B", 4m, "USD"));
            await ctx.SaveChangesAsync();
        }

        // Re-using key-A for the same order must be rejected by the alternate key.
        using (var ctx = ContextFor(db))
        {
            ctx.OrderRefunds.Add(new OrderRefund(5, "key-A", 3m, "USD"));
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.SaveChangesAsync());
        }
    }
}

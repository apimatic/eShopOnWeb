using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// The duplicate-charge guard rests on the store refusing a second claim row with the same key. These tests
/// race two request scopes (two DbContexts) against the same store.
/// </summary>
public class OrderPaymentStoreClaimTests
{
    private readonly DbContextOptions<CatalogContext> _options = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"claims-{Guid.NewGuid()}")
        .Options;

    private async Task<int> SeedOrderAsync()
    {
        await using var context = new CatalogContext(_options);
        var order = new OrderBuilder().WithDefaultValues();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task SecondConcurrentPaymentClaimIsRefused()
    {
        var orderId = await SeedOrderAsync();
        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);
        var firstStore = new OrderPaymentStore(first);
        var secondStore = new OrderPaymentStore(second);

        // Both requests read the order before either claims.
        var firstOrder = (await firstStore.GetOrderAsync(orderId, CancellationToken.None))!;
        var secondOrder = (await secondStore.GetOrderAsync(orderId, CancellationToken.None))!;
        firstOrder.StartPaymentAttempt(1000, "USD", DateTimeOffset.UtcNow);
        secondOrder.StartPaymentAttempt(1000, "USD", DateTimeOffset.UtcNow);

        Assert.True(await firstStore.TrySaveClaimAsync(firstOrder, CancellationToken.None));
        Assert.False(await secondStore.TrySaveClaimAsync(secondOrder, CancellationToken.None));

        await using var verify = new CatalogContext(_options);
        var stored = await verify.PaymentAttempts.Where(a => a.OrderId == orderId).ToListAsync();
        Assert.Single(stored);
    }

    [Fact]
    public async Task SecondConcurrentRefundClaimIsRefused()
    {
        var orderId = await SeedOrderAsync();
        await using (var setup = new CatalogContext(_options))
        {
            var store = new OrderPaymentStore(setup);
            var order = (await store.GetOrderAsync(orderId, CancellationToken.None))!;
            var attempt = order.StartPaymentAttempt(1000, "USD", DateTimeOffset.UtcNow);
            order.RecordPaymentOutcome(attempt, PaymentAttemptStatus.Authorised, "PSP1", "Authorised", null, null, null, null,
                Array.Empty<CapturedProviderResponse>(), DateTimeOffset.UtcNow);
            Assert.True(await store.TrySaveClaimAsync(order, CancellationToken.None));
        }

        await using var first = new CatalogContext(_options);
        await using var second = new CatalogContext(_options);
        var firstStore = new OrderPaymentStore(first);
        var secondStore = new OrderPaymentStore(second);
        var firstOrder = (await firstStore.GetOrderAsync(orderId, CancellationToken.None))!;
        var secondOrder = (await secondStore.GetOrderAsync(orderId, CancellationToken.None))!;

        // Each refund fits on its own; together they would exceed what was paid.
        firstOrder.StartRefund(6m, 600, RefundReason.Return, null, DateTimeOffset.UtcNow);
        secondOrder.StartRefund(6m, 600, RefundReason.Return, null, DateTimeOffset.UtcNow);

        Assert.True(await firstStore.TrySaveClaimAsync(firstOrder, CancellationToken.None));
        Assert.False(await secondStore.TrySaveClaimAsync(secondOrder, CancellationToken.None));

        await using var verify = new CatalogContext(_options);
        Assert.Single(await verify.OrderRefunds.Where(r => r.OrderId == orderId).ToListAsync());
    }
}

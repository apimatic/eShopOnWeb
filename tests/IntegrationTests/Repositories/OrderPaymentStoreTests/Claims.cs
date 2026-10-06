using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.OrderPaymentStoreTests;

/// <summary>
/// The claim is the store refusing a second row with the same primary key — two requests with their own
/// unit of work, racing on the same history, must not both get through.
/// </summary>
public class Claims
{
    private readonly DbContextOptions<CatalogContext> _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(databaseName: $"PaymentClaims-{Guid.NewGuid()}")
        .Options;

    private async Task<int> SeedOrderAsync()
    {
        await using var context = new CatalogContext(_dbOptions);
        var order = new OrderBuilder().WithDefaultValues();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task SecondPaymentAttemptWithTheSameNumberIsRefused()
    {
        var orderId = await SeedOrderAsync();
        await using var first = new CatalogContext(_dbOptions);
        await using var second = new CatalogContext(_dbOptions);

        var firstClaimed = await new OrderPaymentStore(first).TryClaimPaymentAttemptAsync(
            new OrderPaymentAttempt(orderId, 1, 1000, "USD", DateTimeOffset.UtcNow), CancellationToken.None);
        var secondClaimed = await new OrderPaymentStore(second).TryClaimPaymentAttemptAsync(
            new OrderPaymentAttempt(orderId, 1, 1000, "USD", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(firstClaimed);
        Assert.False(secondClaimed);
        Assert.Single(await new OrderPaymentStore(second).ReadPaymentAttemptsAsync(orderId, CancellationToken.None));
    }

    [Fact]
    public async Task SecondRefundWithTheSameSequenceIsRefused()
    {
        var orderId = await SeedOrderAsync();
        await using var first = new CatalogContext(_dbOptions);
        await using var second = new CatalogContext(_dbOptions);

        var firstClaimed = await new OrderPaymentStore(first).TryClaimRefundAsync(
            new OrderRefund(orderId, 1, "PSP", 500, "USD", null, "admin", DateTimeOffset.UtcNow), CancellationToken.None);
        var secondClaimed = await new OrderPaymentStore(second).TryClaimRefundAsync(
            new OrderRefund(orderId, 1, "PSP", 700, "USD", null, "admin", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(firstClaimed);
        Assert.False(secondClaimed);
        var stored = Assert.Single(await new OrderPaymentStore(second).ReadRefundsAsync(orderId, CancellationToken.None));
        Assert.Equal(500, stored.AmountMinorUnits);
    }

    [Fact]
    public async Task ClaimsAreLoadedWithTheOrder()
    {
        var orderId = await SeedOrderAsync();
        await using (var context = new CatalogContext(_dbOptions))
        {
            await new OrderPaymentStore(context).TryClaimPaymentAttemptAsync(
                new OrderPaymentAttempt(orderId, 1, 1000, "USD", DateTimeOffset.UtcNow), CancellationToken.None);
        }

        await using var fresh = new CatalogContext(_dbOptions);
        var order = await new OrderPaymentStore(fresh).GetOrderWithPaymentsAsync(orderId, CancellationToken.None);

        Assert.NotNull(order);
        Assert.Single(order!.PaymentAttempts);
        Assert.Equal(OrderPaymentStatus.PaymentPending, order.PaymentStatus);
    }
}

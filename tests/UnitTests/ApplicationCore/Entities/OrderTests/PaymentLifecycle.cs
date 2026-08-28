using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void CapturesAndSupportsDistinctPartialRefundsWithoutExceedingCapture()
    {
        var order = CreateOrder();
        order.RecordAuthorization("USD", "paypal-order", "APPROVED", "authorization",
            "CREATED", 20m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", 20m, 1m, 19m);

        order.RecordRefund("refund-1", "key-1", "COMPLETED", 5m);
        order.RecordRefund("refund-2", "key-2", "COMPLETED", 10m);

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(15m, order.Refunds.Sum(x => x.Amount));
        Assert.Throws<InvalidOperationException>(() =>
            order.RecordRefund("refund-3", "key-3", "COMPLETED", 6m));
    }

    [Fact]
    public void RejectsReusedRefundKeyAtAggregateBoundary()
    {
        var order = CreateOrder();
        order.RecordAuthorization("USD", "paypal-order", "APPROVED", "authorization",
            "CREATED", 20m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        order.RecordCapture("capture", "COMPLETED", 20m, 1m, 19m);
        order.RecordRefund("refund-1", "same-key", "COMPLETED", 5m);

        Assert.Throws<InvalidOperationException>(() =>
            order.RecordRefund("refund-2", "same-key", "COMPLETED", 5m));
    }

    private static Order CreateOrder() => new(
        "buyer@example.com",
        new Address("1 Main St", "Seattle", "WA", "US", "98101"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Test item", "item.png"), 20m, 1)
        });
}

using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderPaymentState
{
    [Fact]
    public void TracksCaptureFeesAndIndependentRefunds()
    {
        var order = CreateOrder();
        order.RecordPayPalOrder("paypal-order", "CREATED", "USD");
        order.RecordAuthorization("authorization", "CREATED", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29), null, "COMPLETED");
        order.RecordCapture("capture", "COMPLETED", 20m, 1.25m, 18.75m, DateTimeOffset.UtcNow);

        var first = order.BeginRefund("first", 5m);
        first.Complete("refund-one", "COMPLETED", 5m, 0m, 5m, DateTimeOffset.UtcNow);
        order.RefreshRefundState();

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(5m, order.RefundedTotal());
        Assert.Equal(1.25m, order.PayPalFee);
        Assert.Equal(18.75m, order.NetProceeds);

        var second = order.BeginRefund("second", 15m);
        second.Complete("refund-two", "COMPLETED", 15m, 0m, 15m, DateTimeOffset.UtcNow);
        order.RefreshRefundState();

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(20m, order.RefundedTotal());
        Assert.Same(first, order.FindRefund("first"));
    }

    [Fact]
    public void CancellingAuthorizationRecordsReleasedHold()
    {
        var order = CreateOrder();
        order.RecordPayPalOrder("paypal-order", "CREATED", "USD");
        order.RecordAuthorization("authorization", "CREATED", null, null, null, "COMPLETED");

        order.Cancel("VOIDED");

        Assert.Equal(OrderPaymentStatus.Voided, order.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
    }

    private static Order CreateOrder() => new("buyer", new Address("street", "city", "state", "US", "12345"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "item", "picture"), 20m, 1)
        });
}

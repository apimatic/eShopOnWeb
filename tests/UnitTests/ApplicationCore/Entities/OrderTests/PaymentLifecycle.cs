using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void RecordsAuthorizationCaptureAndPartialThenFullRefund()
    {
        var order = CreateOrder();
        var now = DateTimeOffset.UtcNow;

        order.RecordAuthorization("paypal-order", "authorization", "CREATED", 20m, now,
            now.AddDays(29), "VISA ending 1111");
        Assert.Equal(PaymentStatus.Authorized, order.PaymentStatus);

        order.RecordCapture("capture", "COMPLETED", 20m, 1.25m, 18.75m, now);
        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(FulfilmentStatus.Fulfilled, order.FulfilmentStatus);
        Assert.Equal(1.25m, order.PayPalFee);
        Assert.Equal(18.75m, order.NetAmount);

        order.AddRefund("one", "refund-one", "COMPLETED", 5m, 0m, 5m, now);
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(5m, order.RefundedAmount);

        order.AddRefund("two", "refund-two", "COMPLETED", 15m, 0m, 15m, now);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(20m, order.RefundedAmount);
    }

    [Fact]
    public void CancellingAuthorizationRecordsVoidedFunds()
    {
        var order = CreateOrder();
        order.RecordAuthorization("paypal-order", "authorization", "CREATED", 20m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), "Card ending 1111");

        order.Cancel(true);

        Assert.Equal(PaymentStatus.Voided, order.PaymentStatus);
        Assert.Equal("VOIDED", order.AuthorizationStatus);
        Assert.Equal(FulfilmentStatus.Cancelled, order.FulfilmentStatus);
    }

    private static Order CreateOrder() => new("buyer", new Address("street", "city", "state", "US", "12345"),
        new List<OrderItem> { new(new CatalogItemOrdered(1, "item", "picture"), 20m, 1) }, "USD");
}

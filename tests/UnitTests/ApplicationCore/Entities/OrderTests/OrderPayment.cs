using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void RecordAuthorizationMarksAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, null, "USD");

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH-1", order.PayPalAuthorizationId);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public void RemainingRefundableIsCappedAtCapturedMinusRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m);
        order.RecordRefund("R-1", "key-1", 4.00m, "COMPLETED");

        Assert.Equal(6.00m, order.RemainingRefundable());
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);

        order.RecordRefund("R-2", "key-2", 6.00m, "COMPLETED");
        Assert.Equal(0m, order.RemainingRefundable());
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsStoredRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m);
        order.RecordRefund("R-1", "idem-1", 2.00m, "COMPLETED");

        var found = order.FindRefundByIdempotencyKey("idem-1");
        Assert.NotNull(found);
        Assert.Equal("R-1", found!.PayPalRefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("other"));
    }
}

using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RecordsAuthorizationAndCaptureAndPartialRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var total = order.Total();

        order.RecordAuthorization(new AuthorizationResult(
            "PAYPAL-ORDER",
            "AUTH-1",
            "CREATED",
            total,
            "USD",
            System.DateTimeOffset.UtcNow,
            System.DateTimeOffset.UtcNow.AddDays(29)), "USD");

        Assert.Equal(OrderStatus.Authorized, order.Status);

        order.RecordCapture(new CaptureResult("CAPTURE-1", "COMPLETED", total, 1.00m, total - 1.00m, "USD"));
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(1.00m, order.PaypalFee);
        Assert.Equal(total - 1.00m, order.NetAmount);

        var first = order.RecordRefund(new RefundGatewayResult("REF-1", "COMPLETED", 1.00m, "USD"), "key-1");
        Assert.Equal("REF-1", first.PayPalRefundId);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(total - 1.00m, order.RefundableRemaining());

        var replay = order.RecordRefund(new RefundGatewayResult("REF-1", "COMPLETED", 1.00m, "USD"), "key-1");
        Assert.Same(first, replay);
        Assert.Equal(1, order.Refunds.Count);

        Assert.Throws<PaymentException>(() =>
            order.RecordRefund(new RefundGatewayResult("REF-X", "COMPLETED", total, "USD"), "key-too-much"));

        order.RecordRefund(new RefundGatewayResult("REF-2", "COMPLETED", total - 1.00m, "USD"), "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void CancelIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordCancellation(false);
        order.RecordCancellation(false);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}

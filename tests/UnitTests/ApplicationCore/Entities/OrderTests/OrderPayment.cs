using System.Collections.Generic;
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
    public void RecordAuthorizationSetsAuthorizedState()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 3.69m, null);
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH-1", order.PayPalAuthorizationId);
        Assert.Equal(3.69m, order.AuthorizedAmount);
    }

    [Fact]
    public void RecordCaptureThenPartialRefundLeavesRemainingBalance()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O", "A", "CREATED", 3.69m, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PaypalFee);
        Assert.Equal(3.28m, order.NetAmount);

        order.RecordRefund("R-1", "COMPLETED", 1.00m, "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var replay = order.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(replay);
        Assert.Equal("R-1", replay!.PayPalRefundId);
    }

    [Fact]
    public void FullRefundConsumesRemainingCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O", "A", "CREATED", 3.69m, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        order.RecordRefund("R-1", "COMPLETED", 3.69m, "full");
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void FailedRefundDoesNotReduceRemaining()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O", "A", "CREATED", 3.69m, null);
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, null, null);
        order.RecordRefund("R-fail", "FAILED", 3.69m, "fail");
        Assert.Equal(3.69m, order.RemainingRefundable());
    }
}

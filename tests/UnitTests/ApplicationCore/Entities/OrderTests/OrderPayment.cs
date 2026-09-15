using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
    public void RecordsAuthorizationAndCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O-1", "A-1", "CREATED", null, "USD");
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.True(order.AlreadyAuthorized());

        order.RecordCapture("C-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        Assert.Equal(OrderPaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.11m, order.PaypalFee);
        Assert.Equal(3.58m, order.NetAmount);
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O-1", "A-1", "CREATED", null, "USD");
        order.RecordCapture("C-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        order.RecordRefund("R-1", 1.00m, "COMPLETED", "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var duplicate = order.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(duplicate);

        Assert.Throws<CheckoutException>(() => order.EnsureCanRefund(3.00m));
    }

    [Fact]
    public void CannotFulfilAfterCancel()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("O-1", "A-1", "CREATED", null, "USD");
        order.RecordVoid("VOIDED");
        Assert.Equal(OrderPaymentStatus.Voided, order.PaymentStatus);
        Assert.Throws<CheckoutException>(() => order.EnsureCanCapture());
    }
}

using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RecordAuthorizationMarksAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "eshop-pay-1");

        Assert.Equal(OrderPaymentStatus.Authorized, order.Status);
        Assert.True(order.AlreadyAuthorized);
        Assert.Equal("AUTH-1", order.AuthorizationId);
    }

    [Fact]
    public void RecordCaptureMarksFulfilledAndExposesFee()
    {
        var order = AuthorizedOrder();

        order.RecordCapture("CAP-1", "COMPLETED", 19.50m, 0.87m, 18.63m, "eshop-capture-1");

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.Status);
        Assert.Equal(19.50m, order.CapturedGross);
        Assert.Equal(0.87m, order.PaypalFee);
        Assert.Equal(18.63m, order.NetAmount);
        Assert.Equal(19.50m, order.RemainingRefundable());
    }

    [Fact]
    public void PartialRefundDoesNotExceedCaptured()
    {
        var order = CapturedOrder();

        order.RecordRefund("REF-1", "COMPLETED", 5m, "key-1");

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.Status);
        Assert.Equal(14.50m, order.RemainingRefundable());
        Assert.Throws<OrderPaymentException>(() => order.RecordRefund("REF-2", "COMPLETED", 15m, "key-2"));
    }

    [Fact]
    public void SameRefundIdempotencyKeyIsReusableViaLookup()
    {
        var order = CapturedOrder();
        order.RecordRefund("REF-1", "COMPLETED", 5m, "key-1");

        var found = order.FindRefundByIdempotencyKey("key-1");

        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PaypalRefundId);
    }

    [Fact]
    public void FullRefundMarksRefunded()
    {
        var order = CapturedOrder();

        order.RecordRefund("REF-1", "COMPLETED", 19.50m, "key-1");

        Assert.Equal(OrderPaymentStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", null, "eshop-pay-1");
        return order;
    }

    private static Order CapturedOrder()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP-1", "COMPLETED", 19.50m, 0.87m, 18.63m, "eshop-capture-1");
        return order;
    }
}

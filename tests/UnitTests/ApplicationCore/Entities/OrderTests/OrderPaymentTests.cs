using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    private static Order NewOrder(decimal unitPrice = 10m, int units = 1)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://pic"), unitPrice, units);
        return new Order("buyer@example.com", new Address("1 St", "City", "ST", "US", "00000"),
            new List<OrderItem> { item });
    }

    [Fact]
    public void NewOrderStartsAwaitingPaymentWithUniqueReference()
    {
        var a = NewOrder();
        var b = NewOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, a.PaymentStatus);
        Assert.False(string.IsNullOrWhiteSpace(a.PaymentReference));
        Assert.NotEqual(a.PaymentReference, b.PaymentReference); // unique per order, even with equal ids
    }

    [Fact]
    public void RecordAuthorizationMovesToAuthorized()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH-1", order.AuthorizationId);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public void RecordCaptureMovesToFulfilledAndSetsRefundable()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(29m, order.CapturedAmount);
        Assert.Equal(1.24m, order.PayPalFee);
        Assert.Equal(27.76m, order.NetAmount);
        Assert.Equal(29m, order.RefundableRemaining());
    }

    [Fact]
    public void RefundsReduceRemainingAndFlipToPartiallyThenFullyRefunded()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        order.AddRefund(new OrderRefund("R1", 10m, "COMPLETED", "key-1"));
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(10m, order.TotalRefunded());
        Assert.Equal(19m, order.RefundableRemaining());

        order.AddRefund(new OrderRefund("R2", 19m, "COMPLETED", "key-2"));
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsPriorRefund()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, null, null);
        var refund = new OrderRefund("R1", 5m, "COMPLETED", "dup-key");
        order.AddRefund(refund);

        Assert.Same(refund, order.FindRefundByIdempotencyKey("dup-key"));
        Assert.Null(order.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CancelMovesToCancelledAndVoidsAuthorization()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD");
        order.Cancel();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.AuthorizationStatus);
    }

    [Fact]
    public void RenewAuthorizationReplacesAuthorizationId()
    {
        var order = NewOrder(29m);
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-5), "USD");
        order.RenewAuthorization("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal("AUTH-2", order.AuthorizationId);
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
    }
}

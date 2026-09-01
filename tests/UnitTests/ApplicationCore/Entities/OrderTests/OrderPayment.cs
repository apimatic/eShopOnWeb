using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    private static Order NewPaidOrder(decimal capturedAmount = 100m)
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, capturedAmount, 1)
        };
        var order = new OrderBuilder().WithItems(items);
        order.MarkPaymentAuthorized("PAYPAL-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        return order;
    }

    [Fact]
    public void NewOrderStartsPendingPayment()
    {
        var order = new OrderBuilder().WithNoItems();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void MarkPaymentAuthorizedIsIdempotentForSameAuthorization()
    {
        var order = NewPaidOrder();

        // Replaying the same authorization must not throw or change state.
        order.MarkPaymentAuthorized("PAYPAL-ORDER-1", "AUTH-1", "CREATED", null, "USD");

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal("AUTH-1", order.AuthorizationId);
    }

    [Fact]
    public void CannotAuthorizeTwiceWithDifferentAuthorization()
    {
        var order = NewPaidOrder();

        Assert.Throws<InvalidOperationException>(() =>
            order.MarkPaymentAuthorized("PAYPAL-ORDER-2", "AUTH-2", "CREATED", null, "USD"));
    }

    [Fact]
    public void CannotFulfilBeforePayment()
    {
        var order = new OrderBuilder().WithNoItems();

        Assert.Throws<InvalidOperationException>(() =>
            order.MarkFulfilled("CAP-1", 100m, 3m, 97m));
    }

    [Fact]
    public void FulfilRecordsCaptureAmounts()
    {
        var order = NewPaidOrder();

        order.MarkFulfilled("CAP-1", 100m, 3.30m, 96.70m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(100m, order.CapturedAmount);
        Assert.Equal(3.30m, order.PayPalFee);
        Assert.Equal(96.70m, order.NetAmount);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = NewPaidOrder();
        order.MarkFulfilled("CAP-1", 100m, 3.30m, 96.70m);

        Assert.Throws<InvalidOperationException>(() => order.MarkCancelled());
    }

    [Fact]
    public void PartialRefundsNeverExceedCapturedAmount()
    {
        var order = NewPaidOrder();
        order.MarkFulfilled("CAP-1", 100m, 3.30m, 96.70m);

        order.AddRefund("REF-1", "key-1", 60m, "COMPLETED");
        order.AddRefund("REF-2", "key-2", 40m, "COMPLETED");

        Assert.Equal(100m, order.TotalRefunded());
        Assert.Equal(0m, order.RefundableAmount());
        Assert.Throws<InvalidOperationException>(() => order.AddRefund("REF-3", "key-3", 0.01m, "COMPLETED"));
    }

    [Fact]
    public void CannotRefundBeforeFulfilment()
    {
        var order = NewPaidOrder();

        Assert.Throws<InvalidOperationException>(() => order.AddRefund("REF-1", "key-1", 10m, "COMPLETED"));
    }
}

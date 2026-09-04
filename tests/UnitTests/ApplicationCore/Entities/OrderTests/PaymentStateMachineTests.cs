using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentStateMachineTests
{
    private static Order NewOrderWithPayment(decimal total = 29m)
    {
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Thing", "pic"), total, 1)
        };
        var order = new Order("buyer@test", new Address("s", "c", "st", "US", "95131"), items);
        var payment = new Payment(total, "USD");
        payment.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        order.Authorize(payment);
        return order;
    }

    [Fact]
    public void NewOrder_StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithNoItems();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void Authorize_SetsAuthorizedStateAndKeepsPayPalIds()
    {
        var order = NewOrderWithPayment();
        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("PP-ORDER", order.Payment!.PayPalOrderId);
        Assert.Equal("AUTH-1", order.Payment.AuthorizationId);
        Assert.True(order.Payment.IsAuthorizationUsable);
    }

    [Fact]
    public void Capture_RecordsAmountsFeeNet_AndAllowsFulfilment()
    {
        var order = NewOrderWithPayment();
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);
        order.MarkFulfilled();

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(PaymentStatus.Captured, order.Payment.Status);
        Assert.Equal(1.24m, order.Payment.FeeAmount);
        Assert.Equal(27.76m, order.Payment.NetAmount);
        Assert.Equal(29m, order.Payment.RefundableAmount);
    }

    [Fact]
    public void PartialRefund_KeepsRefundableAmountInBounds()
    {
        var order = NewOrderWithPayment();
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        order.Payment.AddRefund("REF-1", 4m, "COMPLETED", DateTimeOffset.UtcNow, "k1");
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.Payment.Status);
        Assert.Equal(25m, order.Payment.RefundableAmount);
    }

    [Fact]
    public void Refund_IdempotencyKeyFindableAndRefundTotalGuarded()
    {
        var order = NewOrderWithPayment();
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);
        order.Payment.AddRefund("REF-1", 29m, "COMPLETED", DateTimeOffset.UtcNow, "k1");

        Assert.Equal(PaymentStatus.Refunded, order.Payment.Status);
        Assert.Equal(0m, order.Payment.RefundableAmount);
        Assert.NotNull(order.Payment.FindRefundByKey("k1"));
        Assert.Null(order.Payment.FindRefundByKey("k2"));
    }

    [Fact]
    public void Cancel_ReleasesAuthorization()
    {
        var order = NewOrderWithPayment();
        order.Payment!.MarkAuthorizationReleased("VOIDED");
        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.AuthorizationReleased, order.Payment.Status);
        Assert.False(order.Payment.IsAuthorizationUsable);
    }

    [Fact]
    public void FulfilledOrder_CannotBeCancelled()
    {
        var order = NewOrderWithPayment();
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);
        order.MarkFulfilled();

        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void Renewal_IncrementsGenerationAndReplacesAuthorization()
    {
        var order = NewOrderWithPayment();
        order.Payment!.IncrementAuthorizationGeneration();
        order.Payment.RecordAuthorization("PP-ORDER-2", "AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(29));

        Assert.Equal(2, order.Payment.AuthorizationGeneration);
        Assert.Equal("AUTH-2", order.Payment.AuthorizationId);
    }
}

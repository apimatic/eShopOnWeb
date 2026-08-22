using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
    public void RecordsAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.PayPalAuthorizationId);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public void PayIsIdempotentOnceAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");

        var ex = Record.Exception(() => order.EnsureCanPay());
        Assert.Null(ex);
        Assert.Equal(OrderStatus.Authorized, order.Status);
    }

    [Fact]
    public void CaptureMovesToFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PayPalFee);
        Assert.Equal(3.28m, order.NetAmount);
    }

    [Fact]
    public void CancelReleasesHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        order.Cancel("VOIDED");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.PayPalAuthorizationStatus);
    }

    [Fact]
    public void CancelIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Cancel();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = FulfilledOrder();
        Assert.Throws<PaymentConflictException>(() => order.Cancel());
    }

    [Fact]
    public void PartialRefundLeavesRemainder()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "COMPLETED", "key-1", 1.00m);

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RefundableRemaining());
    }

    [Fact]
    public void RefundIdempotencyKeyDoesNotRefundTwice()
    {
        var order = FulfilledOrder();
        var first = order.RecordRefund("R1", "COMPLETED", "same-key", 1.00m);
        var second = order.RecordRefund("R1", "COMPLETED", "same-key", 1.00m);

        Assert.Same(first, second);
        Assert.Equal(1.00m, order.RefundedTotal());
    }

    [Fact]
    public void DistinctPartialRefundsAreAllowed()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "COMPLETED", "key-1", 1.00m);
        order.RecordRefund("R2", "COMPLETED", "key-2", 1.00m);

        Assert.Equal(2.00m, order.RefundedTotal());
        Assert.Equal(1.69m, order.RefundableRemaining());
    }

    [Fact]
    public void CannotRefundBeyondCapturedAmount()
    {
        var order = FulfilledOrder();
        Assert.Throws<PaymentConflictException>(() => order.RecordRefund("R1", "COMPLETED", "key-1", 99m));
    }

    [Fact]
    public void FullRefundMarksRefunded()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "COMPLETED", "key-1", 3.69m);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void AnotherShopperCannotAct()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<PaymentForbiddenException>(() => order.EnsureOwnedBy("someone-else"));
    }

    private static Order FulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("AUTH-1", "CREATED", null, null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        return order;
    }
}

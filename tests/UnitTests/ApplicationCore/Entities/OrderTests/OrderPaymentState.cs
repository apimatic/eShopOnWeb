using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void StartsAwaitingPaymentWhenCheckoutBegins()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AwaitPayment();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void RecordsAuthorizationOnce()
    {
        var order = AuthorizedOrder();
        order.RecordAuthorization("ORDER2", "COMPLETED", "AUTH2", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), "USD");

        Assert.Equal("AUTH1", order.Payment.AuthorizationId);
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
    }

    [Fact]
    public void CapturesThenTracksFees()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(3.69m, order.Payment.CapturedAmount);
        Assert.Equal(0.41m, order.Payment.PaypalFee);
        Assert.Equal(3.28m, order.Payment.NetProceeds);
    }

    [Fact]
    public void RejectsRefundBeyondCapturedAmount()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");
        order.RecordRefund("R1", "COMPLETED", 2.00m, "USD", "key-1");

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(1.69m, order.RemainingRefundable());
        Assert.Throws<PaymentConflictException>(() =>
            order.RecordRefund("R2", "COMPLETED", 2.00m, "USD", "key-2"));
    }

    [Fact]
    public void RepeatsRefundUnderSameIdempotencyKey()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");
        var first = order.RecordRefund("R1", "COMPLETED", 1.00m, "USD", "same-key");
        var second = order.RecordRefund("R1", "COMPLETED", 1.00m, "USD", "same-key");

        Assert.Same(first, second);
        Assert.Equal(1.00m, order.RefundedTotal());
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
    }

    [Fact]
    public void AllowsDistinctPartialRefundsUntilCapturedAmountIsExhausted()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");
        order.RecordRefund("R1", "COMPLETED", 1.50m, "USD", "key-a");
        order.RecordRefund("R2", "COMPLETED", 2.19m, "USD", "key-b");

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
        Assert.Equal(2, order.Refunds.Count);
    }

    [Fact]
    public void VoidingFulfilledOrderIsRejected()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.41m, 3.28m, "USD");

        Assert.Throws<PaymentConflictException>(() => order.VoidAuthorization());
    }

    [Fact]
    public void CancelReleasesAuthorizedOrder()
    {
        var order = AuthorizedOrder();
        order.VoidAuthorization("VOIDED");

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.Payment.AuthorizationStatus);
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AwaitPayment();
        order.RecordAuthorization(
            "ORDER1",
            "COMPLETED",
            "AUTH1",
            "CREATED",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29),
            "USD");
        return order;
    }
}

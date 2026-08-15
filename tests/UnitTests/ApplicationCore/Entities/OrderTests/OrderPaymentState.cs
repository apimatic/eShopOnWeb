using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

/// <summary>
/// Unit tests for the additive payment state machine on the <see cref="Order"/> aggregate. These are
/// pure domain tests — no PayPal — asserting the transitions and the refund invariants the API relies
/// on for correctness.
/// </summary>
public class OrderPaymentState
{
    private static Order NewAuthorizedOrder(out Order order)
    {
        order = new OrderBuilder().WithDefaultValues(); // total = 1.23 * 3 = 3.69
        order.SetAuthorized("PP-ORD", "AUTH-1", "USD", DateTimeOffset.UtcNow.AddDays(29), savedPaymentMethodId: null);
        return order;
    }

    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void SetAuthorizedMovesToAuthorizedAndRecordsPayPalState()
    {
        NewAuthorizedOrder(out var order);

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("PP-ORD", order.PayPalOrderId);
        Assert.Equal("AUTH-1", order.PayPalAuthorizationId);
        Assert.Equal("USD", order.PaymentCurrency);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        NewAuthorizedOrder(out var order);
        Assert.Throws<OrderPaymentException>(() =>
            order.SetAuthorized("PP-ORD-2", "AUTH-2", "USD", null, null));
    }

    [Fact]
    public void FulfilCapturesAndRecordsSettlement()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", capturedAmount: 3.69m, payPalFee: 0.41m, netAmount: 3.28m);

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal("CAP-1", order.PayPalCaptureId);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PayPalFee);
        Assert.Equal(3.28m, order.NetAmount);
        Assert.Equal(3.69m, order.RefundableRemaining());
    }

    [Fact]
    public void CannotFulfilBeforeAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<OrderPaymentException>(() => order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m));
    }

    [Fact]
    public void CanCancelWhileAuthorized()
    {
        NewAuthorizedOrder(out var order);
        order.SetCancelled();
        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m);
        Assert.Throws<OrderPaymentException>(() => order.SetCancelled());
    }

    [Fact]
    public void PartialRefundLeavesPartiallyRefundedAndReducesRemaining()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m);

        order.AddRefund("key-1", "REF-1", 1.00m, "COMPLETED");

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(1.00m, order.TotalRefunded());
        Assert.Equal(2.69m, order.RefundableRemaining());
    }

    [Fact]
    public void TwoDistinctPartialRefundsToFullMarkRefunded()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m);

        order.AddRefund("key-1", "REF-1", 1.00m, "COMPLETED");
        order.AddRefund("key-2", "REF-2", 2.69m, "COMPLETED");

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m);

        Assert.Throws<OrderPaymentException>(() =>
            order.AddRefund("key-1", "REF-1", 4.00m, "COMPLETED"));
    }

    [Fact]
    public void CannotRefundBeforeFulfilment()
    {
        NewAuthorizedOrder(out var order);
        Assert.Throws<OrderPaymentException>(() =>
            order.AddRefund("key-1", "REF-1", 1.00m, "COMPLETED"));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsRecordedRefund()
    {
        NewAuthorizedOrder(out var order);
        order.SetFulfilled("CAP-1", 3.69m, 0m, 3.69m);
        order.AddRefund("key-1", "REF-1", 1.00m, "COMPLETED");

        var found = order.FindRefundByIdempotencyKey("key-1");
        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PayPalRefundId);
        Assert.Null(order.FindRefundByIdempotencyKey("nope"));
    }

    [Fact]
    public void ReauthorizeSwapsAuthorizationId()
    {
        NewAuthorizedOrder(out var order);
        var newExpiry = DateTimeOffset.UtcNow.AddDays(29);

        order.SetReauthorized("AUTH-NEW", newExpiry);

        Assert.Equal("AUTH-NEW", order.PayPalAuthorizationId);
        Assert.Equal(newExpiry, order.AuthorizationExpiresAt);
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
    }
}

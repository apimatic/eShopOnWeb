using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Order NewOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void BeginAuthorizationSetsStatusAndPayment()
    {
        var order = NewOrder();

        var payment = order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null,
            "AUTH-1", "CREATED", Now, Now.AddDays(3));

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Same(payment, order.Payment);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
        Assert.Equal(Now, order.Payment.OriginalAuthorizationCreateTime);
    }

    [Fact]
    public void BeginAuthorizationTwiceThrows()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));

        Assert.Throws<InvalidOrderStateException>(() =>
            order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-2", "req-2", null, "AUTH-2", "CREATED", Now, Now.AddDays(3)));
    }

    [Fact]
    public void MarkFulfilledWithoutAuthorizationThrows()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOrderStateException>(() =>
            order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 1m, order.Total() - 1m, "cap-req", Now));
    }

    [Fact]
    public void MarkFulfilledRecordsCaptureAndAdvancesStatus()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));

        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0.50m, order.Total() - 0.50m, "cap-req", Now);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(0.50m, order.Payment.PayPalFeeAmount);
    }

    [Fact]
    public void RecordReauthorizationReplacesAuthorizationButKeepsOriginalCreateTime()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));

        order.RecordReauthorization("AUTH-2", "CREATED", Now.AddDays(10), Now.AddDays(13));

        Assert.Equal("AUTH-2", order.Payment!.AuthorizationId);
        Assert.Equal(Now, order.Payment.OriginalAuthorizationCreateTime); // unchanged - governs the 29-day ceiling
        Assert.Equal(Now.AddDays(13), order.Payment.AuthorizationExpirationTime);
    }

    [Fact]
    public void MarkCancelledFromAwaitingPaymentNeedsNoPayment()
    {
        var order = NewOrder();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkCancelledAfterAuthorizationVoidsPayment()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));

        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void MarkCancelledAfterFulfilmentThrows()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", Now);

        Assert.Throws<InvalidOrderStateException>(() => order.MarkCancelled());
    }

    [Fact]
    public void ApplyPartialRefundMovesToPartiallyRefunded()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", Now);

        var refundAmount = order.Total() / 2;
        order.ApplyRefund("REF-1", refundAmount, "COMPLETED", "idem-1", Now);

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(refundAmount, order.Payment!.RefundedAmount);
        Assert.Equal(refundAmount, order.Payment.RemainingRefundableAmount);
    }

    [Fact]
    public void ApplyRefundCoveringFullCaptureMovesToRefunded()
    {
        var order = NewOrder();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", Now, Now.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", Now);

        order.ApplyRefund("REF-1", order.Total(), "COMPLETED", "idem-1", Now);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment!.RemainingRefundableAmount);
    }

    [Fact]
    public void ApplyRefundBeforeFulfilmentThrows()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOrderStateException>(() => order.ApplyRefund("REF-1", 1m, "COMPLETED", "idem-1", Now));
    }
}

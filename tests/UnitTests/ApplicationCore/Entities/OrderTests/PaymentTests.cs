using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentTests
{
    private static Payment NewPayment(decimal authorized = 100m) =>
        new("PPORDER1", "USD", authorized, "Visa ending 1111", "eshop-order-1-abc");

    [Fact]
    public void AuthorizationStateIsTracked()
    {
        var payment = NewPayment();
        Assert.False(payment.IsAuthorized);

        payment.SetAuthorization("AUTH1", "CREATED");

        Assert.True(payment.IsAuthorized);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal("CREATED", payment.AuthorizationStatus);
    }

    [Fact]
    public void CaptureRecordsBreakdownAndTimestamp()
    {
        var payment = NewPayment();
        payment.SetAuthorization("AUTH1", "CREATED");

        payment.SetCapture("CAP1", "COMPLETED", grossAmount: 100m, payPalFee: 3.20m, netAmount: 96.80m);

        Assert.True(payment.IsCaptured);
        Assert.Equal(100m, payment.CapturedGrossAmount);
        Assert.Equal(3.20m, payment.PayPalFee);
        Assert.Equal(96.80m, payment.NetAmount);
        Assert.NotNull(payment.CapturedAt);
    }

    [Fact]
    public void RefundableRemainingDecreasesWithEachRefund()
    {
        var payment = NewPayment();
        payment.SetCapture("CAP1", "COMPLETED", 100m, 0m, 100m);

        Assert.Equal(100m, payment.RefundableRemaining());

        payment.AddRefund("R1", 30m, "COMPLETED", "key-1");
        Assert.Equal(70m, payment.RefundableRemaining());
        Assert.Equal(30m, payment.TotalRefunded());

        payment.AddRefund("R2", 70m, "COMPLETED", "key-2");
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var payment = NewPayment();
        payment.SetCapture("CAP1", "COMPLETED", 100m, 0m, 100m);
        var refund = payment.AddRefund("R1", 10m, "COMPLETED", "key-1");

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }
}

public class OrderStatusTransitionTests
{
    private static Order AuthorizedOrder(decimal authorized = 100m)
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment("PPORDER1", "USD", authorized, "Visa ending 1111", "eshop-order-1-abc");
        payment.SetAuthorization("AUTH1", "CREATED");
        order.MarkAuthorized(payment);
        return order;
    }

    [Fact]
    public void MarkAuthorizedAttachesPaymentAndSetsStatus()
    {
        var order = AuthorizedOrder();
        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    [Fact]
    public void MarkFulfilledThenPartialRefundIsPartiallyRefunded()
    {
        var order = AuthorizedOrder();
        order.MarkFulfilled();
        order.Payment!.SetCapture("CAP1", "COMPLETED", 100m, 0m, 100m);

        order.Payment.AddRefund("R1", 40m, "COMPLETED", "k1");
        order.MarkRefundApplied();

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public void FullRefundMakesOrderRefunded()
    {
        var order = AuthorizedOrder();
        order.MarkFulfilled();
        order.Payment!.SetCapture("CAP1", "COMPLETED", 100m, 0m, 100m);

        order.Payment.AddRefund("R1", 100m, "COMPLETED", "k1");
        order.MarkRefundApplied();

        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void MarkCancelledSetsCancelled()
    {
        var order = AuthorizedOrder();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}

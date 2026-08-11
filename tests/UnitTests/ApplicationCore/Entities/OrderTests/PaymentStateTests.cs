using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentStateTests
{
    private static Order NewOrder() => new OrderBuilder().WithDefaultValues();

    [Fact]
    public void NewOrderAwaitsPaymentWithNoPaymentAndAReference()
    {
        var order = NewOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
        Assert.False(string.IsNullOrWhiteSpace(order.PaymentReference));
    }

    [Fact]
    public void RecordAuthorizationMovesToAuthorizedAndCarriesPayPalState()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", "USD");

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
        Assert.Equal("PPO-1", order.Payment!.PayPalOrderId);
        Assert.Equal("AUTH-1", order.Payment.AuthorizationId);
        Assert.Equal(order.Total(), order.Payment.Amount); // hold equals the order total to the cent
        Assert.Equal("USD", order.Payment.Currency);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", "USD");
        Assert.Throws<InvalidOperationException>(() => order.RecordAuthorization("PPO-2", "AUTH-2", "CREATED", "USD"));
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOperationException>(() => order.RecordFulfilment("CAP-1", "COMPLETED", 10m, 1m, 9m));
    }

    [Fact]
    public void FulfilCapturesAndRecordsBreakdown()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", "USD");
        order.RecordFulfilment("CAP-1", "COMPLETED", 32.50m, 1.33m, 31.17m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(32.50m, order.Payment.CapturedAmount);
        Assert.Equal(1.33m, order.Payment.PayPalFee);
        Assert.Equal(31.17m, order.Payment.NetAmount);
        Assert.Equal(32.50m, order.Payment.RefundableRemaining);
    }

    [Fact]
    public void CancelRequiresAuthorizedState()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOperationException>(() => order.RecordCancellation());

        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", "USD");
        order.RecordCancellation();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotFulfilAfterCancellation()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", "USD");
        order.RecordCancellation();
        Assert.Throws<InvalidOperationException>(() => order.RecordFulfilment("CAP-1", "COMPLETED", 10m, 1m, 9m));
    }

    private static Payment CapturedPayment(decimal captured = 32.50m)
    {
        var payment = new Payment("PPO-1", "AUTH-1", "CREATED", captured, "USD");
        payment.RecordCapture("CAP-1", "COMPLETED", captured, 1m, captured - 1m);
        return payment;
    }

    [Fact]
    public void PartialRefundsAccumulateAndNeverExceedCapture()
    {
        var payment = CapturedPayment(32.50m);
        payment.AddRefund(new Refund("R1", "k1", 10m, "COMPLETED"));
        payment.AddRefund(new Refund("R2", "k2", 5m, "COMPLETED"));

        Assert.Equal(15m, payment.RefundedAmount);
        Assert.Equal(17.50m, payment.RefundableRemaining);

        // A refund beyond the remaining balance is rejected.
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund(new Refund("R3", "k3", 100m, "COMPLETED")));
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new Payment("PPO-1", "AUTH-1", "CREATED", 10m, "USD");
        Assert.Throws<InvalidOperationException>(() => payment.AddRefund(new Refund("R1", "k1", 1m, "COMPLETED")));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var payment = CapturedPayment();
        var refund = new Refund("R1", "key-abc", 10m, "COMPLETED");
        payment.AddRefund(refund);

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-abc"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void FailedRefundDoesNotCountAgainstCapture()
    {
        var payment = CapturedPayment(20m);
        payment.AddRefund(new Refund("R1", "k1", 20m, "FAILED"));

        // A FAILED refund did not move money, so the full capture remains refundable.
        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Equal(20m, payment.RefundableRemaining);
    }
}

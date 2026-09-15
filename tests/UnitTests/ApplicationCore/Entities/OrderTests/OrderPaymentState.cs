using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Payment NewAuthorizedPayment(decimal amount)
    {
        var payment = new Payment("PPORDER1", "USD", amount, "VISA", "1111");
        payment.RecordAuthorization("AUTH1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        return payment;
    }

    [Fact]
    public void NewOrderIsAwaitingPaymentWithNoPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void SetAuthorizedMovesToAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();

        order.SetAuthorized(NewAuthorizedPayment(order.Total()));

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void CannotAuthorizeTwice()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorized(NewAuthorizedPayment(order.Total()));

        Assert.Throws<PaymentException>(() => order.SetAuthorized(NewAuthorizedPayment(order.Total())));
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<PaymentException>(() => order.SetFulfilled("CAP1", "COMPLETED", order.Total(), 1m, 1m));
    }

    [Fact]
    public void FulfilRecordsCaptureAndBreakdown()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorized(NewAuthorizedPayment(order.Total()));

        order.SetFulfilled("CAP1", "COMPLETED", order.Total(), 0.81m, order.Total() - 0.81m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP1", order.Payment!.CaptureId);
        Assert.Equal(0.81m, order.Payment.PayPalFee);
        Assert.Equal(order.Total() - 0.81m, order.Payment.NetAmount);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorized(NewAuthorizedPayment(order.Total()));
        order.SetFulfilled("CAP1", "COMPLETED", order.Total(), 0m, order.Total());

        Assert.Throws<PaymentException>(() => order.SetCancelled());
    }

    [Fact]
    public void CancelBeforeFulfilmentVoidsHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorized(NewAuthorizedPayment(order.Total()));

        order.SetCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void CannotRefundBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.SetAuthorized(NewAuthorizedPayment(order.Total()));

        Assert.Throws<PaymentException>(() => order.EnsureCanRefund(1m));
    }

    [Fact]
    public void RefundCannotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var total = order.Total();
        order.SetAuthorized(NewAuthorizedPayment(total));
        order.SetFulfilled("CAP1", "COMPLETED", total, 0m, total);

        Assert.Throws<PaymentException>(() => order.EnsureCanRefund(total + 0.01m));
    }

    [Fact]
    public void PartialThenRemainingRefundTransitionsToRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var total = order.Total();
        order.SetAuthorized(NewAuthorizedPayment(total));
        order.SetFulfilled("CAP1", "COMPLETED", total, 0m, total);

        var half = Math.Round(total / 2, 2);
        order.EnsureCanRefund(half);
        order.RecordRefund(new PaymentRefund("k1", "REF1", "COMPLETED", half, DateTimeOffset.UtcNow));
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);

        var remaining = order.Payment!.RefundableRemaining;
        order.EnsureCanRefund(remaining);
        order.RecordRefund(new PaymentRefund("k2", "REF2", "COMPLETED", remaining, DateTimeOffset.UtcNow));

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment.RefundableRemaining);
        Assert.Equal(total, order.Payment.TotalRefunded);
    }
}

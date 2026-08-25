using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class AddRefund
{
    private static OrderPayment CapturedPayment(decimal capturedAmount)
    {
        var payment = new OrderPayment(1, 100m, "USD", "paypal-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        payment.RecordCapture("capture-1", "COMPLETED", capturedAmount, 3.5m, capturedAmount - 3.5m);
        return payment;
    }

    [Fact]
    public void FirstPartialRefundReducesRemainingAmount()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", "COMPLETED", 40m, "key-1");

        Assert.Equal(60m, payment.RemainingRefundableAmount);
        Assert.Equal(40m, payment.RefundedAmount);
    }

    [Fact]
    public void TwoDistinctPartialRefundsAreBothHonoured()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("refund-1", "COMPLETED", 40m, "key-1");
        payment.AddRefund("refund-2", "COMPLETED", 60m, "key-2");

        Assert.Equal(0m, payment.RemainingRefundableAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void RefundExceedingCapturedAmountThrows()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("refund-1", "COMPLETED", 60m, "key-1");

        Assert.Throws<RefundExceedsCapturedAmountException>(() => payment.AddRefund("refund-2", "COMPLETED", 60m, "key-2"));
    }

    [Fact]
    public void RefundExceedingCapturedAmountDoesNotChangeState()
    {
        var payment = CapturedPayment(100m);

        try
        {
            payment.AddRefund("refund-1", "COMPLETED", 150m, "key-1");
        }
        catch (RefundExceedsCapturedAmountException)
        {
            // expected
        }

        Assert.Equal(0m, payment.RefundedAmount);
        Assert.Empty(payment.Refunds);
    }
}

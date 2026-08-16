using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentRefunds
{
    private static OrderPayment Authorized(decimal amount = 29.00m) =>
        new(orderId: 1, buyerId: "buyer-1", currency: "USD", amount: amount,
            paymentReference: "ESHOP-run-1", payPalOrderId: "PPO1", authorizationId: "AUTH1",
            authorizationStatus: "CREATED", authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(29));

    private static OrderPayment Captured(decimal amount = 29.00m)
    {
        var p = Authorized(amount);
        p.MarkCaptured("CAP1", "COMPLETED", amount, payPalFee: 1.24m, netAmount: amount - 1.24m);
        return p;
    }

    [Fact]
    public void NewPaymentStartsAuthorized()
    {
        var p = Authorized();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal(0m, p.TotalRefunded());
    }

    [Fact]
    public void CaptureRecordsBreakdownAndAllowsRefundUpToCaptured()
    {
        var p = Captured(29.00m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal(29.00m, p.CapturedAmount);
        Assert.Equal(1.24m, p.PayPalFee);
        Assert.Equal(27.76m, p.NetAmount);
        Assert.Equal(29.00m, p.RefundableRemaining());
    }

    [Fact]
    public void PartialRefundLeavesRemainderAndMarksPartiallyRefunded()
    {
        var p = Captured(29.00m);
        p.AddRefund("R1", 10.00m, "COMPLETED", "keyA");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10.00m, p.TotalRefunded());
        Assert.Equal(19.00m, p.RefundableRemaining());
    }

    [Fact]
    public void FullyRefundedWhenSumReachesCaptured()
    {
        var p = Captured(29.00m);
        p.AddRefund("R1", 10.00m, "COMPLETED", "keyA");
        p.AddRefund("R2", 19.00m, "COMPLETED", "keyB");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsPriorRefund()
    {
        var p = Captured(29.00m);
        var r = p.AddRefund("R1", 10.00m, "COMPLETED", "keyA");

        Assert.Same(r, p.FindRefundByIdempotencyKey("keyA"));
        Assert.Null(p.FindRefundByIdempotencyKey("otherKey"));
    }

    [Fact]
    public void CancelMarksVoidedAndNoMoneyMoves()
    {
        var p = Authorized();
        p.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
        Assert.Null(p.CapturedAmount);
    }
}

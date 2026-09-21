using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    private static OrderPayment AuthorizedPayment(decimal authorized = 100m) =>
        new("PPORDER1", "AUTH1", "CREATED", authorized, "USD", null);

    private static OrderPayment CapturedPayment(decimal captured = 100m)
    {
        var payment = AuthorizedPayment(captured);
        payment.RecordCapture("CAP1", "COMPLETED", captured, 3.20m, captured - 3.20m);
        return payment;
    }

    [Fact]
    public void RecordCapture_SetsCaptureFieldsAndFullRefundable()
    {
        var payment = CapturedPayment(100m);

        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(100m, payment.CapturedAmount);
        Assert.Equal(3.20m, payment.PayPalFee);
        Assert.Equal(96.80m, payment.NetAmount);
        Assert.Equal(100m, payment.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_PartialThenPartial_TracksRemaining()
    {
        var payment = CapturedPayment(100m);

        payment.AddRefund("R1", 30m, "COMPLETED", "key-1");
        Assert.Equal(30m, payment.TotalRefunded());
        Assert.Equal(70m, payment.RefundableRemaining());

        payment.AddRefund("R2", 20m, "COMPLETED", "key-2");
        Assert.Equal(50m, payment.TotalRefunded());
        Assert.Equal(50m, payment.RefundableRemaining());
    }

    [Fact]
    public void AddRefund_BeyondCapturedAmount_Throws()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R1", 80m, "COMPLETED", "key-1");

        var ex = Assert.Throws<PaymentOperationException>(() => payment.AddRefund("R2", 25m, "COMPLETED", "key-2"));
        Assert.Contains("exceeds", ex.Message);
        // The rejected refund must not have been recorded.
        Assert.Equal(80m, payment.TotalRefunded());
    }

    [Fact]
    public void AddRefund_WithoutCapture_Throws()
    {
        var payment = AuthorizedPayment(100m);
        Assert.Throws<PaymentOperationException>(() => payment.AddRefund("R1", 10m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsMatch()
    {
        var payment = CapturedPayment(100m);
        var refund = payment.AddRefund("R1", 10m, "COMPLETED", "key-1");

        Assert.Same(refund, payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void FailedRefund_DoesNotConsumeRefundableAmount()
    {
        var payment = CapturedPayment(100m);
        payment.AddRefund("R1", 100m, "FAILED", "key-1");

        // A failed refund returned no money, so the full amount remains refundable.
        Assert.Equal(0m, payment.TotalRefunded());
        Assert.Equal(100m, payment.RefundableRemaining());
    }
}

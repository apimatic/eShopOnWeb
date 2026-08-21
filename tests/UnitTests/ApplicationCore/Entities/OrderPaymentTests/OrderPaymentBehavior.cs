using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentBehavior
{
    private static OrderPayment NewPayment(decimal amount = 39m) =>
        new OrderPayment(orderId: 1, buyerId: "shopper@example.com", amount: amount, currencyCode: "USD");

    private static OrderPayment CapturedPayment(decimal amount = 39m)
    {
        var payment = NewPayment(amount);
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);
        payment.MarkCaptured("CAP1", "COMPLETED", amount, 1.5m, amount - 1.5m);
        return payment;
    }

    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithInvoiceReference()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.False(string.IsNullOrWhiteSpace(payment.InvoiceReference));
    }

    [Fact]
    public void TwoPayments_HaveDistinctInvoiceReferences()
    {
        Assert.NotEqual(NewPayment().InvoiceReference, NewPayment().InvoiceReference);
    }

    [Fact]
    public void MarkAuthorized_SetsHoldState()
    {
        var payment = NewPayment();

        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH1", payment.AuthorizationId);
        Assert.Equal("PPORDER", payment.PayPalOrderId);
    }

    [Fact]
    public void MarkCaptured_RecordsAmountsAndStatus()
    {
        var payment = CapturedPayment();

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP1", payment.CaptureId);
        Assert.Equal(39m, payment.CapturedGrossAmount);
        Assert.Equal(1.5m, payment.PayPalFeeAmount);
        Assert.Equal(37.5m, payment.NetAmount);
        Assert.Equal(39m, payment.RefundableRemaining());
    }

    [Fact]
    public void PartialRefund_LeavesPartiallyRefunded_AndReducesRefundable()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-1", 10m, "REF1", "COMPLETED");

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.TotalRefunded());
        Assert.Equal(29m, payment.RefundableRemaining());
    }

    [Fact]
    public void FullRefund_LeavesRefunded()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-1", 39m, "REF1", "COMPLETED");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void TwoPartialRefunds_Accumulate()
    {
        var payment = CapturedPayment();

        payment.AddRefund("key-1", 10m, "REF1", "COMPLETED");
        payment.AddRefund("key-2", 5m, "REF2", "COMPLETED");

        Assert.Equal(15m, payment.TotalRefunded());
        Assert.Equal(24m, payment.RefundableRemaining());
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void Refund_ExceedingCaptured_Throws_AndNeverExceedsCapture()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-1", 30m, "REF1", "COMPLETED");

        // 30 already refunded; a further 20 would exceed the 39 captured.
        Assert.Throws<InvalidPaymentOperationException>(() => payment.AddRefund("key-2", 20m, "REF2", "COMPLETED"));
        Assert.Equal(30m, payment.TotalRefunded());
        Assert.Equal(9m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refund_BeforeCapture_Throws()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        Assert.Throws<InvalidPaymentOperationException>(() => payment.AddRefund("key-1", 5m, "REF1", "COMPLETED"));
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExisting()
    {
        var payment = CapturedPayment();
        payment.AddRefund("key-1", 10m, "REF1", "COMPLETED");

        Assert.NotNull(payment.FindRefundByIdempotencyKey("key-1"));
        Assert.Null(payment.FindRefundByIdempotencyKey("other"));
    }

    [Fact]
    public void MarkVoided_SetsVoided()
    {
        var payment = NewPayment();
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null);

        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }
}

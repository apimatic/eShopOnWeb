using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment CapturedPayment(decimal amount = 17m)
    {
        var payment = new OrderPayment(1, "buyer@example.com", amount, "USD");
        payment.MarkAuthorized("PP-ORDER", "PP-AUTH", "CREATED", null, null);
        payment.MarkCaptured("PP-CAP", "COMPLETED", amount, 0.93m, amount - 0.93m);
        return payment;
    }

    [Fact]
    public void NewPayment_IsAwaitingPayment_WithUniqueIdentifiers()
    {
        var payment = new OrderPayment(5, "buyer@example.com", 10m, "USD");

        Assert.Equal(PaymentStatus.AwaitingPayment, payment.Status);
        Assert.NotNull(payment.PayPalInvoiceId);
        Assert.StartsWith("eshop-order-5-", payment.PayPalInvoiceId);
        Assert.False(string.IsNullOrEmpty(payment.AuthorizeRequestId));
        Assert.False(string.IsNullOrEmpty(payment.CaptureRequestId));
    }

    [Fact]
    public void PrepareRetry_RotatesIdentifiers()
    {
        var payment = new OrderPayment(5, "buyer@example.com", 10m, "USD");
        var invoice = payment.PayPalInvoiceId;
        var authId = payment.AuthorizeRequestId;

        payment.PrepareRetry();

        Assert.NotEqual(invoice, payment.PayPalInvoiceId);
        Assert.NotEqual(authId, payment.AuthorizeRequestId);
    }

    [Fact]
    public void PartialRefund_ThenRemainder_TransitionsStatus()
    {
        var payment = CapturedPayment();

        payment.AddRefund(new PaymentRefund("k1", 5m, "R1", "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(12m, payment.RefundableRemaining());

        payment.AddRefund(new PaymentRefund("k2", 12m, "R2", "COMPLETED"));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refund_ExceedingRemaining_Throws()
    {
        var payment = CapturedPayment();
        payment.AddRefund(new PaymentRefund("k1", 10m, "R1", "COMPLETED"));

        var ex = Assert.Throws<PaymentStateException>(() =>
            payment.AddRefund(new PaymentRefund("k2", 10m, "R2", "COMPLETED")));
        Assert.Contains("exceeds", ex.Message);
        Assert.Equal(7m, payment.RefundableRemaining());
    }

    [Fact]
    public void Refund_BeforeCapture_Throws()
    {
        var payment = new OrderPayment(1, "buyer@example.com", 17m, "USD");
        payment.MarkAuthorized("PP-ORDER", "PP-AUTH", "CREATED", null, null);

        Assert.Throws<PaymentStateException>(() =>
            payment.AddRefund(new PaymentRefund("k1", 1m, "R1", "COMPLETED")));
    }

    [Fact]
    public void FindRefundByIdempotencyKey_ReturnsExisting()
    {
        var payment = CapturedPayment();
        payment.AddRefund(new PaymentRefund("dup-key", 5m, "R1", "COMPLETED"));

        var found = payment.FindRefundByIdempotencyKey("dup-key");
        Assert.NotNull(found);
        Assert.Equal("R1", found!.PayPalRefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void Cancel_MarksVoided()
    {
        var payment = new OrderPayment(1, "buyer@example.com", 17m, "USD");
        payment.MarkAuthorized("PP-ORDER", "PP-AUTH", "CREATED", null, null);

        payment.MarkCancelled();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("VOIDED", payment.AuthorizationStatus);
    }

    [Theory]
    [InlineData("eshop-order-42-abc123", 42)]
    [InlineData("eshop-order-7", 7)]
    [InlineData("something-else", null)]
    [InlineData(null, null)]
    public void InvoiceReference_ParsesOrderId(string? invoiceId, int? expected)
    {
        Assert.Equal(expected, OrderInvoiceReference.TryParseOrderId(invoiceId));
    }
}

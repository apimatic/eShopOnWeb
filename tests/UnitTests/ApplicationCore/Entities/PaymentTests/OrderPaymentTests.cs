using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewPayment() => new(orderId: 42, buyerId: "buyer@x.com", amount: 39.00m, currency: "USD");

    [Fact]
    public void StartsPendingWithUniqueInvoiceId()
    {
        var p = NewPayment();
        Assert.Equal(PaymentStatus.PendingPayment, p.Status);
        Assert.Equal(39.00m, p.Amount);
        Assert.StartsWith("eshop-42-", p.InvoiceId);
        Assert.NotEqual(p.InvoiceId, NewPayment().InvoiceId); // unique per instance
    }

    [Fact]
    public void EnsureAuthorizeRequestIdIsStable()
    {
        var p = NewPayment();
        var first = p.EnsureAuthorizeRequestId();
        Assert.Equal(first, p.EnsureAuthorizeRequestId());
    }

    [Fact]
    public void MarkAuthorizedThenCapturedTracksPayPalState()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "2030-01-01T00:00:00Z");
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal("AUTH1", p.AuthorizationId);

        p.MarkCaptured("CAP1", 39.00m, 1.50m, 37.50m);
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal("CAP1", p.CaptureId);
        Assert.Equal(1.50m, p.PayPalFee);
        Assert.Equal(37.50m, p.NetAmount);
        Assert.Equal(39.00m, p.RefundableRemaining());
    }

    [Fact]
    public void PartialThenFullRefundMovesStatusAndNeverExceedsCaptured()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", null);
        p.MarkCaptured("CAP1", 39.00m, 1.50m, 37.50m);

        p.AddRefund(new PaymentRefund("key-1", "REF1", 10.00m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(10.00m, p.RefundedAmount);
        Assert.Equal(29.00m, p.RefundableRemaining());
        Assert.NotNull(p.FindRefundByKey("key-1"));
        Assert.Null(p.FindRefundByKey("missing"));

        p.AddRefund(new PaymentRefund("key-2", "REF2", 29.00m, "COMPLETED"));
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(0.00m, p.RefundableRemaining());
    }

    [Fact]
    public void CancelMarksCancelled()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", null);
        p.MarkCancelled();
        Assert.Equal(PaymentStatus.Cancelled, p.Status);
    }
}

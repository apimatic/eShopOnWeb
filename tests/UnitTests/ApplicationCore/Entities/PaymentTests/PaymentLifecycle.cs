using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewPayment_StartsAwaitingPayment_WithUniqueInvoiceId()
    {
        var a = new Payment(1, "buyer@test", "USD", 10m);
        var b = new Payment(1, "buyer@test", "USD", 10m);

        Assert.Equal(PaymentStatus.AwaitingPayment, a.Status);
        Assert.False(string.IsNullOrWhiteSpace(a.InvoiceId));
        Assert.NotEqual(a.InvoiceId, b.InvoiceId); // unique per order instance
    }

    [Fact]
    public void MarkAuthorized_RecordsHold()
    {
        var payment = new Payment(1, "buyer@test", "USD", 10m);

        payment.MarkAuthorized("AUTH-1", "CREATED");

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal("CREATED", payment.AuthorizationStatus);
    }

    [Fact]
    public void MarkAuthorizationRenewed_UpdatesAuthorizationId()
    {
        var payment = new Payment(1, "buyer@test", "USD", 10m);
        payment.MarkAuthorized("AUTH-1", "CREATED");

        payment.MarkAuthorizationRenewed("AUTH-2", "CREATED");

        Assert.Equal("AUTH-2", payment.AuthorizationId);
    }

    [Fact]
    public void MarkCaptured_RecordsWhatPayPalReported()
    {
        var payment = new Payment(1, "buyer@test", "USD", 20m);
        payment.MarkAuthorized("AUTH-1", "CREATED");

        payment.MarkCaptured("CAP-1", "COMPLETED", 20m, 1.18m, 18.82m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(20m, payment.CapturedAmount);
        Assert.Equal(1.18m, payment.PayPalFee);
        Assert.Equal(18.82m, payment.NetAmount);
    }

    [Fact]
    public void MarkVoided_ReleasesHold()
    {
        var payment = new Payment(1, "buyer@test", "USD", 20m);
        payment.MarkAuthorized("AUTH-1", "CREATED");

        payment.MarkVoided();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }
}

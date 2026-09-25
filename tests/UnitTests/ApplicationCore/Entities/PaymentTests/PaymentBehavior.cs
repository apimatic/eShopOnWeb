using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentBehavior
{
    private static Payment NewCapturedPayment()
    {
        var p = new Payment(orderId: 1, buyerId: "buyer@x.com", currencyCode: "USD",
            authorizedAmount: 36m, paymentMethodKind: "card", paymentMethodDescription: "Card ending 1111");
        p.RecordAuthorization("PPORDER", "AUTH1", "CREATED", null);
        p.RecordCapture("CAP1", "COMPLETED", 36m, 1.42m, 34.58m);
        return p;
    }

    [Fact]
    public void Records_authorization_and_capture()
    {
        var p = NewCapturedPayment();
        Assert.True(p.IsAuthorized);
        Assert.True(p.IsCaptured);
        Assert.Equal("CAP1", p.CaptureId);
        Assert.Equal(36m, p.CapturedAmount);
        Assert.Equal(1.42m, p.PayPalFee);
        Assert.Equal(34.58m, p.NetAmount);
        Assert.Equal(36m, p.RemainingRefundable);
    }

    [Fact]
    public void Refunds_reduce_remaining_refundable_and_never_exceed_capture()
    {
        var p = NewCapturedPayment();
        p.RecordRefund("R1", 10m, "COMPLETED", "key-1");
        Assert.Equal(10m, p.RefundedAmount);
        Assert.Equal(26m, p.RemainingRefundable);

        p.RecordRefund("R2", 5m, "COMPLETED", "key-2");
        Assert.Equal(15m, p.RefundedAmount);
        Assert.Equal(21m, p.RemainingRefundable);
    }

    [Fact]
    public void FindRefundByKey_supports_idempotency()
    {
        var p = NewCapturedPayment();
        p.RecordRefund("R1", 10m, "COMPLETED", "key-1");
        Assert.NotNull(p.FindRefundByKey("key-1"));
        Assert.Equal("R1", p.FindRefundByKey("key-1")!.RefundId);
        Assert.Null(p.FindRefundByKey("key-unknown"));
    }
}

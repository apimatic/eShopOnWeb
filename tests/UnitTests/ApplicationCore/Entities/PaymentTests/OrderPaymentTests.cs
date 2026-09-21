using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    private static OrderPayment NewPayment() => new(orderId: 42, buyerId: "shopper@x.com", amount: 20.00m, currencyCode: "USD");

    [Fact]
    public void StartsAwaitingPayment()
    {
        var p = NewPayment();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void MarkAuthorizedThenFulfilledTracksState()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");
        Assert.Equal(OrderPaymentStatus.Authorized, p.Status);

        p.MarkFulfilled("CAP1", "COMPLETED", 20.00m, 0.98m, 19.02m);
        Assert.Equal(OrderPaymentStatus.Fulfilled, p.Status);
        Assert.Equal(20.00m, p.CapturedAmount);
        Assert.Equal(0.98m, p.PayPalFee);
        Assert.Equal(19.02m, p.NetAmount);
        Assert.Equal(20.00m, p.RefundableRemaining());
    }

    [Fact]
    public void PartialThenFullRefundMovesToRefundedAndCapsRefundable()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");
        p.MarkFulfilled("CAP1", "COMPLETED", 20.00m, 0.98m, 19.02m);

        p.AddRefund(new PaymentRefund("R1", 5.00m, "COMPLETED", "key-1"));
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(5.00m, p.TotalRefunded());
        Assert.Equal(15.00m, p.RefundableRemaining());

        p.AddRefund(new PaymentRefund("R2", 15.00m, "COMPLETED", "key-2"));
        Assert.Equal(OrderPaymentStatus.Refunded, p.Status);
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void FindRefundByKeyReturnsExistingForDuplicateKey()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");
        p.MarkFulfilled("CAP1", "COMPLETED", 20.00m, null, null);
        var refund = new PaymentRefund("R1", 5.00m, "COMPLETED", "idem-99");
        p.AddRefund(refund);

        Assert.Same(refund, p.FindRefundByKey("idem-99"));
        Assert.Null(p.FindRefundByKey("other-key"));
    }

    [Fact]
    public void CanceledReleasesHold()
    {
        var p = NewPayment();
        p.MarkAuthorized("PPORDER", "AUTH1", "CREATED", null, "VISA", "1111");
        p.MarkCanceled();
        Assert.Equal(OrderPaymentStatus.Canceled, p.Status);
        Assert.Equal("VOIDED", p.AuthorizationStatus);
    }
}

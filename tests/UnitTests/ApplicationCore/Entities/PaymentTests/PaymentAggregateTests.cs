using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentAggregateTests
{
    private static Payment NewAuthorized() =>
        new Payment(
            orderId: 1,
            buyerId: "buyer@test.com",
            currency: "USD",
            amount: 100m,
            payPalOrderId: "PP-ORDER-1",
            authorizationId: "AUTH-1",
            authorizationStatus: "CREATED",
            authorizationExpiresAt: DateTimeOffset.UtcNow.AddDays(3),
            savedPaymentMethodId: null);

    private static Payment NewCaptured()
    {
        var p = NewAuthorized();
        p.MarkCaptured("CAP-1", "COMPLETED", 100m, 3m, 97m);
        return p;
    }

    [Fact]
    public void NewPaymentStartsAuthorized()
    {
        var p = NewAuthorized();
        Assert.Equal(PaymentStatus.Authorized, p.Status);
        Assert.Equal(100m, p.Amount);
        Assert.Equal("AUTH-1", p.AuthorizationId);
    }

    [Fact]
    public void CaptureRecordsFeeAndNetAndFlipsStatus()
    {
        var p = NewCaptured();
        Assert.Equal(PaymentStatus.Captured, p.Status);
        Assert.Equal(100m, p.CapturedAmount);
        Assert.Equal(3m, p.PayPalFee);
        Assert.Equal(97m, p.NetAmount);
        Assert.NotNull(p.CapturedAt);
    }

    [Fact]
    public void CannotCaptureTwice()
    {
        var p = NewCaptured();
        Assert.Throws<InvalidOperationException>(() => p.MarkCaptured("CAP-2", "COMPLETED", 100m, 3m, 97m));
    }

    [Fact]
    public void CannotVoidAfterCapture()
    {
        var p = NewCaptured();
        Assert.Throws<InvalidOperationException>(() => p.MarkVoided());
    }

    [Fact]
    public void PartialRefundLeavesPartiallyRefunded()
    {
        var p = NewCaptured();
        p.AddRefund("R1", 40m, "COMPLETED", "key-1");

        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(40m, p.TotalRefunded());
        Assert.Equal(60m, p.RefundableRemaining());
    }

    [Fact]
    public void TwoDistinctPartialRefundsAccumulate()
    {
        var p = NewCaptured();
        p.AddRefund("R1", 40m, "COMPLETED", "key-1");
        p.AddRefund("R2", 60m, "COMPLETED", "key-2");

        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(100m, p.TotalRefunded());
        Assert.Equal(0m, p.RefundableRemaining());
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var p = NewCaptured();
        p.AddRefund("R1", 70m, "COMPLETED", "key-1");

        var ex = Assert.Throws<InvalidOperationException>(() => p.AddRefund("R2", 40m, "COMPLETED", "key-2"));
        Assert.Contains("exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The rejected refund did not mutate state.
        Assert.Equal(70m, p.TotalRefunded());
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var p = NewAuthorized();
        Assert.Throws<InvalidOperationException>(() => p.AddRefund("R1", 10m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsThePriorRefund()
    {
        var p = NewCaptured();
        p.AddRefund("R1", 25m, "COMPLETED", "dup-key");

        var found = p.FindRefundByIdempotencyKey("dup-key");
        Assert.NotNull(found);
        Assert.Equal("R1", found!.RefundId);
        Assert.Null(p.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void RenewAuthorizationReplacesTheHold()
    {
        var p = NewAuthorized();
        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
        p.RenewAuthorization("AUTH-2", "CREATED", newExpiry);

        Assert.Equal("AUTH-2", p.AuthorizationId);
        Assert.Equal(PaymentStatus.Authorized, p.Status);
    }
}

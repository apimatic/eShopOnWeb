using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Payment NewHold(decimal amount = 29.00m) =>
        new("USD", amount, "PP-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), "VISA ****1111");

    [Fact]
    public void NewOrderIsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void AuthorizeMovesToAuthorizedAndRecordsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());

        Assert.Equal(PaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void CannotFulfilBeforeAuthorization()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<InvalidOperationException>(() => order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m));
    }

    [Fact]
    public void FulfilMovesToPaidAndRecordsCapture()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(1.24m, order.Payment!.PayPalFee);
        Assert.Equal(27.76m, order.Payment!.NetAmount);
    }

    [Fact]
    public void CancelAfterAuthorizationVoidsTheHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Cancel();

        Assert.Equal(PaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);
        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void PartialRefundThenFullRefundTransitionsStatus()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        order.AddRefund(new PaymentRefund("k1", "REF-1", 10m, "COMPLETED"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(10m, order.Payment!.RefundedAmount);
        Assert.Equal(19m, order.Payment!.RefundableRemaining);

        order.AddRefund(new PaymentRefund("k2", "REF-2", 19m, "COMPLETED"));
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(29m, order.Payment!.RefundedAmount);
        Assert.Equal(0m, order.Payment!.RefundableRemaining);
    }

    [Fact]
    public void RefundBeyondCapturedAmountIsRejected()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);

        Assert.Throws<InvalidOperationException>(() => order.AddRefund(new PaymentRefund("k", "REF", 30m, "COMPLETED")));
    }

    [Fact]
    public void CannotRefundBeforeFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        Assert.Throws<InvalidOperationException>(() => order.AddRefund(new PaymentRefund("k", "REF", 5m, "COMPLETED")));
    }

    [Fact]
    public void FindRefundByKeyReturnsRecordedRefund()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize(NewHold());
        order.Fulfil("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m);
        order.AddRefund(new PaymentRefund("dedupe-key", "REF-1", 5m, "COMPLETED"));

        Assert.NotNull(order.Payment!.FindRefundByKey("dedupe-key"));
        Assert.Null(order.Payment!.FindRefundByKey("other-key"));
    }
}

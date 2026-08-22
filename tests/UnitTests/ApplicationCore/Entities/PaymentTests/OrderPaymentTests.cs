using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class OrderPaymentTests
{
    [Fact]
    public void RemainingRefundableStartsAtCapturedAmount()
    {
        var payment = AuthorizedPayment();
        payment.RecordCapture("CAP-1", "COMPLETED", 19.50m, 0.87m, 18.63m);

        Assert.Equal(19.50m, payment.RemainingRefundable);
        Assert.Equal(OrderPaymentStatus.Fulfilled, payment.Status);
        Assert.Equal(0.87m, payment.PaypalFee);
        Assert.Equal(18.63m, payment.NetProceeds);
    }

    [Fact]
    public void PartialRefundReducesRemainingAndDoesNotAllowOverRefund()
    {
        var payment = AuthorizedPayment();
        payment.RecordCapture("CAP-1", "COMPLETED", 19.50m, 0.87m, 18.63m);

        var first = payment.RecordRefund("REF-1", 5.00m, "COMPLETED", "key-1");
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(14.50m, payment.RemainingRefundable);
        Assert.Equal("key-1", first.IdempotencyKey);

        Assert.Same(first, payment.FindRefundByIdempotencyKey("key-1"));

        var ex = Assert.Throws<CheckoutException>(() => payment.RecordRefund("REF-2", 20m, "COMPLETED", "key-2"));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void FullRefundMarksPaymentRefunded()
    {
        var payment = AuthorizedPayment();
        payment.RecordCapture("CAP-1", "COMPLETED", 10.00m, 0.50m, 9.50m);

        payment.RecordRefund("REF-1", 10.00m, "COMPLETED", "full");

        Assert.Equal(OrderPaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RemainingRefundable);
        var ex = Assert.Throws<CheckoutException>(() => payment.EnsureCanRefund());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void PayingTwiceIsIdempotentOnceAuthorized()
    {
        var payment = AuthorizedPayment();
        Assert.Equal(OrderPaymentStatus.Authorized, payment.Status);
        Assert.Throws<CheckoutException>(() => payment.BeginPayAttempt());
    }

    [Fact]
    public void CancelAfterFulfilmentIsRejected()
    {
        var payment = AuthorizedPayment();
        payment.RecordCapture("CAP-1", "COMPLETED", 10.00m, 0.40m, 9.60m);

        var ex = Assert.Throws<CheckoutException>(() => payment.RecordCancellation());
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public void OtherShopperCannotSeePayment()
    {
        var payment = AuthorizedPayment();
        var ex = Assert.Throws<CheckoutException>(() => payment.EnsureOwnedBy("someone-else"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void PayPalMoneyFormatsToCents()
    {
        Assert.Equal("19.50", PayPalMoney.Format(19.5m, "USD"));
        Assert.Equal("8.50", PayPalMoney.Format(8.5m, "USD"));
        Assert.Equal("12.00", PayPalMoney.Format(12m, "USD"));
        Assert.Equal(19.50m, PayPalMoney.Parse("19.50"));
    }

    [Fact]
    public void ReconciliationMatchesOnCustomIdAndCaptureId()
    {
        var payment = AuthorizedPayment();
        payment.AttachPayPalOrder("ORDER-PP", "order:1", "ESHOP-1-abc");
        payment.RecordCapture("CAP-99", "COMPLETED", 10m, 0.3m, 9.7m);

        Assert.True(CheckoutService.Matches(payment, new PayPalReportedTransaction
        {
            TransactionId = "CAP-99"
        }));
        Assert.True(CheckoutService.Matches(payment, new PayPalReportedTransaction
        {
            TransactionId = "other",
            CustomField = "order:1"
        }));
        Assert.False(CheckoutService.Matches(payment, new PayPalReportedTransaction
        {
            TransactionId = "nope"
        }));
    }

    private static OrderPayment AuthorizedPayment()
    {
        var payment = new OrderPayment(1, "demouser@microsoft.com", 19.50m, "USD");
        payment.BeginPayAttempt();
        payment.RecordAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow);
        return payment;
    }
}

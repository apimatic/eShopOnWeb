using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentTests
{
    [Fact]
    public void TracksCaptureAndDistinctRefunds()
    {
        var payment = new OrderPayment("USD", "invoice-1", "reference-1");
        payment.BeginAuthorizationAttempt();
        payment.RecordPayPalOrder("ORDER-1", "CREATED");
        payment.RecordAuthorization("AUTH-1", "CREATED", 19.50m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        payment.RecordCapture("CAPTURE-1", "COMPLETED", 19.50m, 1.00m, 18.50m,
            DateTimeOffset.UtcNow);

        var first = payment.StartRefund(Guid.NewGuid(), "partial", 5.00m);
        first.RecordPayPalResult("REFUND-1", "COMPLETED", 5.00m, 0.20m, 4.80m);
        payment.RefreshRefundState();

        Assert.Equal(PaymentState.PartiallyRefunded, payment.State);
        Assert.Equal(5.00m, payment.RefundedAmount);
        Assert.Same(first, payment.FindRefund("partial"));

        var second = payment.StartRefund(Guid.NewGuid(), "remaining", 14.50m);
        second.RecordPayPalResult("REFUND-2", "COMPLETED", 14.50m, 0.80m, 13.70m);
        payment.RefreshRefundState();

        Assert.Equal(PaymentState.Refunded, payment.State);
        Assert.Equal(19.50m, payment.RefundedAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void ReusesAnInProgressAuthorizationAttempt()
    {
        var payment = new OrderPayment("USD", "invoice-2", "reference-2");

        var first = payment.BeginAuthorizationAttempt();
        var retry = payment.BeginAuthorizationAttempt();

        Assert.Equal(first, retry);
        Assert.Equal(1, payment.AuthorizationAttempt);
        Assert.True(payment.AuthorizationAttemptInProgress);
    }
}

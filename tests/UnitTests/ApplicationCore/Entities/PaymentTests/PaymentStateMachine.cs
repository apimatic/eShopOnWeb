using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.PaymentTests;

public class PaymentStateMachine
{
    private static Payment NewPayment() =>
        new(1, "buyer@example.com", 25m, "USD", "eshop-1-20260828120000");

    [Fact]
    public void AnAuthorizedPaymentCannotBeAuthorizedAgain()
    {
        var payment = NewPayment();
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);

        // This is what makes a double-click safe even if it gets past the service's own check.
        Assert.Throws<OrderStateException>(() => payment.BeginAuthorizationAttempt());
    }

    [Fact]
    public void AFailedAuthorizationCanBeRetriedUnderAFreshAttemptNumber()
    {
        var payment = NewPayment();

        Assert.Equal(1, payment.BeginAuthorizationAttempt());
        payment.MarkFailed();

        // A new attempt number is what keeps the retry's idempotency key distinct from the failed
        // attempt's, so the processor treats it as a new request rather than replaying the decline.
        Assert.Equal(2, payment.BeginAuthorizationAttempt());
    }

    [Fact]
    public void AnUnsettledOutcomeFreezesThePaymentUntilItIsReconciled()
    {
        var payment = NewPayment();
        payment.BeginAuthorizationAttempt();
        payment.MarkOutcomeUnknown();

        var frozen = Assert.Throws<OrderStateException>(() => payment.BeginAuthorizationAttempt());
        Assert.Contains("reconcil", frozen.Message, StringComparison.OrdinalIgnoreCase);

        payment.ClearReconciliationHold();
        Assert.Equal(2, payment.BeginAuthorizationAttempt());
    }

    [Fact]
    public void CaptureRequiresAHold_AndVoidIsImpossibleOnceCaptured()
    {
        var payment = NewPayment();

        Assert.Throws<OrderStateException>(() => payment.RecordCapture("PP-CAP", "COMPLETED", 25m, 1m, 24m));

        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);
        payment.RecordCapture("PP-CAP", "COMPLETED", 25m, 1m, 24m);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Throws<OrderStateException>(() => payment.MarkVoided());
    }

    [Fact]
    public void ReauthorizingReplacesTheHoldAndBumpsTheAttemptNumber()
    {
        var payment = NewPayment();
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1));

        Assert.True(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));

        var attempt = payment.BeginReauthorizationAttempt();
        payment.RecordReauthorization("PP-AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal(2, attempt);
        Assert.Equal("PP-AUTH-2", payment.AuthorizationId);
        Assert.False(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AHoldWithNoRecordedExpiryIsNotTreatedAsStale()
    {
        var payment = NewPayment();
        payment.BeginAuthorizationAttempt();
        payment.RecordAuthorization("PP-ORDER", "PP-AUTH", "CREATED", null);

        Assert.False(payment.IsAuthorizationStale(DateTimeOffset.UtcNow));
    }
}

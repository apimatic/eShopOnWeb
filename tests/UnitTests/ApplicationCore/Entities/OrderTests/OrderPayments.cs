using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayments
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    private static Order NewOrder() => new OrderBuilder().WithDefaultValues();

    private static Order PaidOrder(long amountMinor = 5000)
    {
        var order = NewOrder();
        var claim = order.ClaimPayment(amountMinor, "USD", Now, Stale);
        order.RecordPaymentOutcome(claim.Attempt.AttemptNumber, PaymentAttemptStatus.Authorised, "PSP1", "Authorised", null, null, null, Now);
        return order;
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, NewOrder().PaymentStatus);
    }

    [Fact]
    public void FirstClaimStartsAnAttemptAndBlocksASecondOne()
    {
        var order = NewOrder();

        var first = order.ClaimPayment(369, "USD", Now, Stale);
        var second = order.ClaimPayment(369, "USD", Now.AddSeconds(1), Stale);

        Assert.Equal(PaymentClaimKind.Send, first.Kind);
        Assert.Equal(PaymentClaimKind.InProgress, second.Kind);
        Assert.Single(order.PaymentAttempts);
        Assert.Equal(OrderPaymentStatus.PaymentPending, order.PaymentStatus);
    }

    [Fact]
    public void ClaimRotatesThePaymentVersion()
    {
        var order = NewOrder();
        var before = order.PaymentVersion;

        order.ClaimPayment(369, "USD", Now, Stale);

        Assert.NotEqual(before, order.PaymentVersion);
    }

    [Fact]
    public void PaidOrderIsNeverClaimedAgain()
    {
        var order = PaidOrder();

        var claim = order.ClaimPayment(5000, "USD", Now, Stale);

        Assert.Equal(PaymentClaimKind.AlreadyPaid, claim.Kind);
        Assert.Single(order.PaymentAttempts);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public void UnknownOutcomeIsResentWithTheSameIdempotencyKey()
    {
        var order = NewOrder();
        var first = order.ClaimPayment(369, "USD", Now, Stale);
        order.RecordPaymentOutcome(1, PaymentAttemptStatus.Unknown, null, null, null, null, null, Now);

        var retry = order.ClaimPayment(369, "USD", Now.AddSeconds(5), Stale);

        Assert.Equal(PaymentClaimKind.Resend, retry.Kind);
        Assert.Equal(first.Attempt.IdempotencyKey, retry.Attempt.IdempotencyKey);
        Assert.Single(order.PaymentAttempts);
    }

    [Fact]
    public void AbandonedInFlightClaimIsResentAfterItGoesStale()
    {
        var order = NewOrder();
        var first = order.ClaimPayment(369, "USD", Now, Stale);

        var retry = order.ClaimPayment(369, "USD", Now + Stale, Stale);

        Assert.Equal(PaymentClaimKind.Resend, retry.Kind);
        Assert.Equal(first.Attempt.IdempotencyKey, retry.Attempt.IdempotencyKey);
    }

    [Fact]
    public void RefusedCardLeavesOrderUnpaidAndAllowsANewAttemptWithANewKey()
    {
        var order = NewOrder();
        var first = order.ClaimPayment(369, "USD", Now, Stale);
        order.RecordPaymentOutcome(1, PaymentAttemptStatus.Refused, "PSP1", "Refused", "Expired Card", "6", "declined", Now);

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);

        var second = order.ClaimPayment(369, "USD", Now, Stale);
        Assert.Equal(PaymentClaimKind.Send, second.Kind);
        Assert.Equal(2, second.Attempt.AttemptNumber);
        Assert.NotEqual(first.Attempt.IdempotencyKey, second.Attempt.IdempotencyKey);
    }

    [Fact]
    public void ProviderPendingOutcomeBlocksAnotherPayment()
    {
        var order = NewOrder();
        order.ClaimPayment(369, "USD", Now, Stale);
        order.RecordPaymentOutcome(1, PaymentAttemptStatus.Pending, "PSP1", "Pending", null, null, null, Now);

        var claim = order.ClaimPayment(369, "USD", Now.AddHours(1), Stale);

        Assert.Equal(PaymentClaimKind.AwaitingProviderOutcome, claim.Kind);
        Assert.Equal(OrderPaymentStatus.PaymentPending, order.PaymentStatus);
    }

    [Fact]
    public void PartialThenRemainingRefundMovesThroughStatuses()
    {
        var order = PaidOrder(5000);

        var partial = order.ClaimRefund(Guid.NewGuid(), "k1", 1050, "damaged", "admin", Now, Stale);
        order.RecordRefundOutcome(partial.Refund.Id, RefundStatus.Received, "R1", null, Now);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(3950, order.RefundableMinor);

        var rest = order.ClaimRefund(Guid.NewGuid(), "k2", null, null, "admin", Now, Stale);
        Assert.Equal(3950, rest.Refund.AmountMinor);
        order.RecordRefundOutcome(rest.Refund.Id, RefundStatus.Received, "R2", null, Now);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0, order.RefundableMinor);
    }

    [Fact]
    public void NeverRefundsBeyondWhatWasPaid()
    {
        var order = PaidOrder(5000);
        order.ClaimRefund(Guid.NewGuid(), "k1", 4000, null, "admin", Now, Stale);

        var ex = Assert.Throws<OrderPaymentException>(() => order.ClaimRefund(Guid.NewGuid(), "k2", 1001, null, "admin", Now, Stale));
        Assert.Equal(OrderPaymentError.ExceedsRefundable, ex.Error);
    }

    [Fact]
    public void InFlightRefundCountsAgainstTheBalance_FailedOneReleasesIt()
    {
        var order = PaidOrder(5000);
        var refund = order.ClaimRefund(Guid.NewGuid(), "k1", 5000, null, "admin", Now, Stale);

        Assert.Throws<OrderPaymentException>(() => order.ClaimRefund(Guid.NewGuid(), "k2", 1, null, "admin", Now, Stale));

        order.RecordRefundOutcome(refund.Refund.Id, RefundStatus.Failed, null, "rejected", Now);
        Assert.Equal(5000, order.RefundableMinor);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public void SameRefundIdReturnsTheExistingRefund()
    {
        var order = PaidOrder(5000);
        var id = Guid.NewGuid();
        var first = order.ClaimRefund(id, "k1", 1000, null, "admin", Now, Stale);
        order.RecordRefundOutcome(id, RefundStatus.Received, "R1", null, Now);

        var again = order.ClaimRefund(id, "k1", 1000, null, "admin", Now, Stale);

        Assert.Equal(RefundClaimKind.Existing, again.Kind);
        Assert.Same(first.Refund, again.Refund);
        Assert.Single(order.Refunds);
    }

    [Fact]
    public void UnpaidOrderCannotBeRefunded()
    {
        var ex = Assert.Throws<OrderPaymentException>(() => NewOrder().ClaimRefund(Guid.NewGuid(), "k", null, null, "admin", Now, Stale));
        Assert.Equal(OrderPaymentError.NotPaid, ex.Error);
    }
}

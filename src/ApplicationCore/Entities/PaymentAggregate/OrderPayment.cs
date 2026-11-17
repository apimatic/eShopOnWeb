using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currency, long amountCents)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountCents = amountCents;
        PaymentReference = $"eshop-{Guid.NewGuid():N}";
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public long AmountCents { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string PaymentReference { get; private set; }

    public string? PaymentInstrumentId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationIdempotencyId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationAttempts { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetAmountCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string? VoidState { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public long RefundedAmountCents =>
        _refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.AmountCents);

    public long RefundableAmountCents =>
        Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public int BeginAuthorizationAttempt()
    {
        AuthorizationAttempts++;
        return AuthorizationAttempts;
    }

    public void BeginAuthorization(string paymentInstrumentId, string idempotencyId)
    {
        PaymentInstrumentId = paymentInstrumentId;
        AuthorizationIdempotencyId = idempotencyId;
        AuthorizationAttempts++;
        Status = PaymentStatus.AwaitingPayment;
    }

    public void MarkAuthorized(string paymentInstrumentId, string authorizationId, string? authorizationState,
        long amountCents, DateTimeOffset? expiresAt)
    {
        PaymentInstrumentId = paymentInstrumentId;
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAt = DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        if (amountCents > 0)
        {
            AmountCents = amountCents;
        }
    }

    public void MarkAuthorizationFailed(string? authorizationState)
    {
        AuthorizationState = authorizationState;
        Status = PaymentStatus.Failed;
    }

    public void MarkCaptured(string captureTransferId, string? captureState, long capturedAmountCents,
        long feeCents, long netAmountCents)
    {
        CaptureTransferId = captureTransferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetAmountCents = netAmountCents;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
        RecalculateRefundStatus();
    }

    public void MarkCanceled(string? voidState)
    {
        VoidState = voidState;
        if (!string.IsNullOrWhiteSpace(voidState))
        {
            AuthorizationState = "CANCELED";
        }
        CanceledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Canceled;
    }

    public PaymentRefund AddRefund(string idempotencyKey, long amountCents, string? finixTransferId, string? state)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, amountCents, finixTransferId, state);
        _refunds.Add(refund);
        RecalculateRefundStatus();
        return refund;
    }

    public void RecalculateRefundStatus()
    {
        if (Status == PaymentStatus.Canceled || Status == PaymentStatus.Failed || Status == PaymentStatus.Authorized)
        {
            return;
        }

        var captured = CapturedAmountCents ?? 0;
        var refunded = RefundedAmountCents;

        if (refunded <= 0)
        {
            if (CapturedAmountCents.HasValue)
            {
                Status = PaymentStatus.Captured;
            }
        }
        else if (refunded >= captured)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}

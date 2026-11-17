using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, long amountCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        Currency = currency;
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string AuthorizationIdempotencyKey { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixCaptureTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public bool UsedSavedCard { get; private set; }

    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetAmountCents { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public string EnsureAuthorizationIdempotencyKey()
    {
        if (string.IsNullOrWhiteSpace(AuthorizationIdempotencyKey))
        {
            AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        return AuthorizationIdempotencyKey;
    }

    public long RefundedAmountCents() =>
        _refunds.Where(r => r.FinixTransferId is not null).Sum(r => r.AmountCents);

    public long RefundableAmountCents() =>
        Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents());

    public void MarkAuthorized(string authorizationId, string state, string paymentInstrumentId, bool usedSavedCard)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = state;
        FinixPaymentInstrumentId = paymentInstrumentId;
        UsedSavedCard = usedSavedCard;
        Status = PaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
        UpdatedAt = AuthorizedAt.Value;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkAuthorizationState(string state)
    {
        FinixAuthorizationState = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(string captureTransferId, string captureState, long capturedAmountCents, long? feeCents)
    {
        FinixCaptureTransferId = captureTransferId;
        FinixCaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetAmountCents = capturedAmountCents - (feeCents ?? 0);
        Status = PaymentStatus.Captured;
        CapturedAt = DateTimeOffset.UtcNow;
        FulfilledAt = CapturedAt;
        UpdatedAt = CapturedAt.Value;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkCaptureState(string state)
    {
        FinixCaptureState = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled()
    {
        Status = PaymentStatus.Canceled;
        CanceledAt = DateTimeOffset.UtcNow;
        UpdatedAt = CanceledAt.Value;
    }

    public void MarkFailed(string? code, string? message)
    {
        Status = PaymentStatus.Failed;
        FailureCode = code;
        FailureMessage = message;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetForRetry()
    {
        Status = PaymentStatus.PendingPayment;
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
        FinixAuthorizationId = null;
        FinixAuthorizationState = null;
        FinixCaptureTransferId = null;
        FinixCaptureState = null;
        FinixPaymentInstrumentId = null;
        UsedSavedCard = false;
        CapturedAmountCents = null;
        FinixFeeCents = null;
        NetAmountCents = null;
        AuthorizedAt = null;
        CapturedAt = null;
        FulfilledAt = null;
        CanceledAt = null;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string idempotencyKey, long amountCents, string finixTransferId, string? state)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, amountCents, finixTransferId, state);
        _refunds.Add(refund);

        var refunded = RefundedAmountCents();
        Status = CapturedAmountCents is not null && refunded >= CapturedAmountCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;

        return refund;
    }

    public void RecordRefundState(string state)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

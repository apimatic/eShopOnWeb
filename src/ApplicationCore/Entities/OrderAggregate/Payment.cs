using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<Refund> _refunds = new();

    private Payment() { }

    public Payment(int orderId, string buyerId, long amountCents, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public OrderPaymentStatus Status { get; private set; }

    public string? PaymentInstrumentId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public string? AuthorizationIdempotencyKey { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureTransferState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetAmountCents { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public long RefundedAmountCents => _refunds
        .Where(r => r.State != RefundState.Failed)
        .Sum(r => r.AmountCents);

    public long RefundableAmountCents => Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public void SetAuthorizationIdempotencyKey(string key)
    {
        AuthorizationIdempotencyKey = key;
    }

    public void PrepareForRetry()
    {
        AuthorizationId = null;
        AuthorizationState = null;
        CaptureTransferId = null;
        CaptureTransferState = null;
        CapturedAmountCents = null;
        FinixFeeCents = null;
        NetAmountCents = null;
        AuthorizedAt = null;
        CapturedAt = null;
        FailureCode = null;
        FailureMessage = null;
        AuthorizationIdempotencyKey = null;
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public void MarkAuthorized(string authorizationId, string? authorizationState, string paymentInstrumentId)
    {
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        PaymentInstrumentId = paymentInstrumentId;
        Status = OrderPaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage)
    {
        Status = OrderPaymentStatus.Failed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        AuthorizationIdempotencyKey = null;
    }

    public void MarkAuthorizationExpired(string? failureCode, string? failureMessage)
    {
        Status = OrderPaymentStatus.AuthorizationExpired;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public void MarkCaptured(
        string transferId,
        string? transferState,
        long capturedAmountCents,
        long finixFeeCents)
    {
        CaptureTransferId = transferId;
        CaptureTransferState = transferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetAmountCents = capturedAmountCents - finixFeeCents;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
    }

    public void UpdateCaptureState(string? transferState, long? finixFeeCents)
    {
        CaptureTransferState = transferState;
        if (finixFeeCents.HasValue)
        {
            FinixFeeCents = finixFeeCents;
            NetAmountCents = (CapturedAmountCents ?? 0) - finixFeeCents.Value;
        }
    }

    public void MarkCaptureFailed(string? failureCode, string? failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }

    public void MarkCancelled(string? authorizationState)
    {
        Status = OrderPaymentStatus.Cancelled;
        AuthorizationState = authorizationState ?? AuthorizationState;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void AddRefund(Refund refund)
    {
        _refunds.Add(refund);
    }

    public void RecalculateRefundStatus()
    {
        if (CapturedAmountCents is null)
        {
            return;
        }

        if (RefundedAmountCents >= CapturedAmountCents.Value)
        {
            Status = OrderPaymentStatus.Refunded;
        }
        else if (RefundedAmountCents > 0)
        {
            Status = OrderPaymentStatus.PartiallyRefunded;
        }
        else
        {
            Status = OrderPaymentStatus.Captured;
        }
    }
}

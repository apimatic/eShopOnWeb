using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Owns the Finix-side state for an eShop order. Card data is never stored here:
/// only Finix resource identifiers, statuses and amounts are persisted.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        AuthorizationIdempotencyId = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public long AuthorizedAmountCents { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long FeeCents { get; private set; }
    public long NetCents { get; private set; }
    public long RefundedAmountCents { get; private set; }

    public string? FinixPaymentInstrumentId { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationIdempotencyId { get; private set; }
    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureStatus { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public long RefundableAmountCents => Math.Max(0, CapturedAmountCents - RefundedAmountCents);

    public void MarkAuthorized(string paymentInstrumentId, string authorizationId, string authorizationStatus, long amountCents)
    {
        FinixPaymentInstrumentId = paymentInstrumentId;
        FinixAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmountCents = amountCents;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage, string authorizationStatus)
    {
        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        AuthorizationStatus = authorizationStatus;
        // A retry must use a fresh Finix idempotency id.
        AuthorizationIdempotencyId = Guid.NewGuid().ToString("N");
    }

    public void MarkCaptured(string transferId, string captureStatus, long capturedAmountCents, long feeCents)
    {
        FinixCaptureTransferId = transferId;
        CaptureStatus = captureStatus;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = Math.Max(0, capturedAmountCents - feeCents);
        Status = PaymentStatus.Captured;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCaptureDetails(string captureStatus, long feeCents)
    {
        CaptureStatus = captureStatus;
        FeeCents = feeCents;
        NetCents = Math.Max(0, CapturedAmountCents - feeCents);
    }

    public void MarkCancelled(string? authorizationStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            AuthorizationStatus = authorizationStatus;
        }

        Status = PaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(long amountCents, string idempotencyKey, string? finixRefundId, string status)
    {
        var refund = new PaymentRefund(amountCents, idempotencyKey, finixRefundId, status);
        _refunds.Add(refund);
        RefundedAmountCents += amountCents;
        Status = RefundedAmountCents >= CapturedAmountCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void MarkRefundFailed(string? failureCode, string? failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }
}

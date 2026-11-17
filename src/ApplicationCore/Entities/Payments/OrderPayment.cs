using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    Canceled,
    PartiallyRefunded,
    Refunded
}

/// <summary>
/// Payment state for an order. The Finix identifiers and states are kept here so that
/// subsequent requests (capture, void, refund) can act on the same Finix resources.
/// </summary>
public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency, long amountCents)
    {
        Currency = currency;
        AmountCents = amountCents;
        Status = PaymentStatus.AwaitingPayment;
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public Order Order { get; private set; } = null!;

    public string Currency { get; private set; }
    public long AmountCents { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// Stable per-payment idempotency key used for the Finix authorization. It survives retries
    /// within the same data store but is unique across fresh databases, so sandbox re-runs do not
    /// collide with an earlier authorization.
    /// </summary>
    public string AuthorizationIdempotencyKey { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public long AuthorizedAmountCents { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long FinixFeeCents { get; private set; }
    public long NetProceedsCents { get; private set; }

    public long RefundedAmountCents { get; private set; }
    public string? PaymentInstrumentId { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }
    public DateTimeOffset? LastUpdatedAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string authorizationId,
        string authorizationState,
        long authorizedAmountCents,
        string paymentInstrumentId)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAmountCents = authorizedAmountCents;
        PaymentInstrumentId = paymentInstrumentId;
        Status = PaymentStatus.Authorized;
        LastError = null;
        AuthorizedAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = AuthorizedAt;
    }

    public void RecordAuthorizationFailure(string error)
    {
        LastError = error;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string transferId,
        string captureState,
        long capturedAmountCents,
        long finixFeeCents)
    {
        FinixCaptureTransferId = transferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = capturedAmountCents - finixFeeCents;
        Status = PaymentStatus.Captured;
        LastError = null;
        CapturedAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = CapturedAt;
    }

    public void RecordCaptureFailure(string error)
    {
        LastError = error;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCancel(string authorizationState)
    {
        AuthorizationState = authorizationState;
        Status = PaymentStatus.Canceled;
        LastError = null;
        CanceledAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = CanceledAt;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }

    public void UpdateRefundedTotal(long refundedAmountCents)
    {
        RefundedAmountCents = refundedAmountCents;
        if (CapturedAmountCents > 0 && refundedAmountCents >= CapturedAmountCents)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (refundedAmountCents > 0)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public long RefundableAmountCents()
    {
        var activeRefunds = _refunds
            .Where(r => !PaymentRefund.IsFailedState(r.State))
            .Sum(r => r.AmountCents);
        return Math.Max(0, CapturedAmountCents - activeRefunds);
    }
}

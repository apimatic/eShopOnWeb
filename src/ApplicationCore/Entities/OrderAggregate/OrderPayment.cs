using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment attached to an <see cref="Order"/>. It carries enough of the state PayPal owns
/// (ids and current status for the hold, the capture and the refunds) that a later request can
/// act on it, not only the request that created it. Owned by the Order aggregate root.
///
/// No card numbers are ever stored here — only a safe, non-sensitive descriptor of the instrument.
/// </summary>
public class OrderPayment
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string currency)
    {
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
    }

    public PaymentStatus Status { get; private set; }

    /// <summary>ISO-4217 currency the payment is denominated in (from configuration).</summary>
    public string Currency { get; private set; }

    // --- Hold (PayPal Orders v2 + authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Safe descriptor of the instrument used (never full card details) ---
    public string? InstrumentDescription { get; private set; }

    // --- Capture (taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value refunded so far across all refunds.</summary>
    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund; never negative.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? instrumentDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        InstrumentDescription = instrumentDescription;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Applied after a stale authorization is renewed (reauthorized) — the id may change.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void RecordCancellation()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    public void RecordRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

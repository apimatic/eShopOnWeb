using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an order: the state PayPal owns (ids and current status for the hold, the capture
/// and the refunds) kept alongside the order so a later request can act on it. Part of the Order
/// aggregate and owned by <see cref="Order"/>.
/// </summary>
public class PaymentRecord
{
    // A stable, per-order external reference used as the PayPal invoice_id / custom_id, and the key
    // reconciliation matches on. Unique across runs so PayPal's invoice-id uniqueness is never violated.
    public string ReferenceId { get; private set; }
    public string Currency { get; private set; }

    // The hold PayPal placed on the buyer's funds.
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // The capture (settlement) — populated at fulfilment with what PayPal reported.
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // Safe-to-display card metadata (never the full number).
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    // The saved card used to pay, when the shopper paid with one (else null for a one-off card).
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRecord() { }
#pragma warning restore CS8618

    public PaymentRecord(
        string referenceId, string currency, string payPalOrderId, string authorizationId,
        string authorizationStatus, decimal authorizedAmount, DateTimeOffset? authorizationExpiresAt,
        string? cardBrand, string? cardLast4, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(referenceId, nameof(referenceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        ReferenceId = referenceId;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    /// <summary>Total of every refund recorded against the capture.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public bool IsCaptured => CaptureId is not null;

    /// <summary>Replaces the hold with a renewed authorization (after a reauthorize).</summary>
    public void ReplaceAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetAuthorizationStatus(string status) => AuthorizationStatus = status;

    /// <summary>Records the settlement reported by PayPal at fulfilment.</summary>
    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal fee, decimal net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund against the capture. Guards that the total refunded never exceeds the captured
    /// amount, so a partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (!IsCaptured)
            throw new InvalidOperationException("Cannot refund an order that has not been captured.");
        if (refund.Amount <= 0m)
            throw new ArgumentException("Refund amount must be positive.", nameof(refund));
        if (refund.Amount > RefundableRemaining)
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable remaining of {RefundableRemaining}.");

        _refunds.Add(refund);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an order. Part of the Order aggregate. Carries enough of the state PayPal
/// owns — ids and current status for the hold (authorization), the capture, and the refunds —
/// that a later request can act on it without replaying the request that started it.
///
/// No full card details are ever stored here; only PayPal-owned identifiers and safe metadata.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, decimal amount, string currency, string reconciliationReference)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Amount = amount;
        Currency = currency;
        ReconciliationReference = reconciliationReference;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The authorized amount (order total to the cent).</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string Currency { get; private set; }

    /// <summary>
    /// The value stamped onto the PayPal transaction as its custom field, so PayPal's reporting can
    /// be lined up against this exact order during reconciliation. Unique per order and per run.
    /// </summary>
    public string ReconciliationReference { get; private set; }

    // ----- The hold (authorization) -----
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- The capture -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // ----- The refunds -----
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    public bool IsCaptured => CaptureId is not null;

    /// <summary>The remaining amount that may still be refunded without exceeding what was captured.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>
    /// Replaces the authorization after a stale hold has been renewed (re-authorized) with PayPal.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (IsCaptured)
            throw new InvalidOperationException("Cannot renew the authorization for an already-captured payment.");

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>Records the result of capturing the authorization at fulfilment.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund against the capture. Guards that the payment is captured and that the
    /// cumulative refunded amount can never exceed the captured amount.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        if (!IsCaptured)
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund amount must be positive.");
        if (amount > RefundableAmount)
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the refundable amount {RefundableAmount:0.00} for this capture.");

        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentState State
    {
        get
        {
            if (AuthorizationStatus == "VOIDED" && !IsCaptured) return PaymentState.Voided;
            if (!IsCaptured) return PaymentState.Authorized;
            if (RefundedAmount <= 0m) return PaymentState.Captured;
            return RefundedAmount >= (CapturedAmount ?? 0m)
                ? PaymentState.Refunded
                : PaymentState.PartiallyRefunded;
        }
    }
}

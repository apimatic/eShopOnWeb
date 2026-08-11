using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state for an <see cref="Order"/>: the ids and current status of the hold
/// (authorization), the capture, and any refunds — enough that a later request can act on the
/// payment, not only the one that started it. No card details are ever stored here.
/// Part of the Order aggregate; created and mutated only through <see cref="Order"/>.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        decimal amount,
        string currency,
        string paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Amount = amount;
        Currency = currency;
        PaymentMethodDescription = paymentMethodDescription;
    }

    /// <summary>The PayPal Checkout order id created to hold the money.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The PayPal authorization id (the hold) used to capture, void or reauthorize.</summary>
    public string AuthorizationId { get; private set; }

    public string AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization hold expires; used to renew a stale hold before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The captured amount as authorized (order total). Set at authorization time.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>Safe, non-sensitive description of the instrument used, e.g. "Visa ending 1111".</summary>
    public string PaymentMethodDescription { get; private set; }

    // Capture ---------------------------------------------------------------
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal captured.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>The fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    // Refunds ---------------------------------------------------------------
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => CaptureId is null && AuthorizationStatus is not ("VOIDED" or "CAPTURED");
    public bool IsCaptured => CaptureId is not null;

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedGross ?? 0m) - TotalRefunded;

    internal void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void RecordCapture(string captureId, string status, decimal gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGross = gross;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
    }

    internal void RecordVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    internal PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        CaptureStatus = RefundableRemaining <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }

    internal PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

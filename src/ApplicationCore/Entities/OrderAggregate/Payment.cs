using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned payment state for an <see cref="Order"/>: the ids and current statuses of the
/// hold (authorization), the capture, and any refunds — enough that a later request can act on the
/// payment, not only the one that started it. No card details are ever stored here.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string currencyCode, decimal authorizedAmount, string payPalOrderId,
        string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt,
        string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        CurrencyCode = currencyCode;
        AuthorizedAmount = authorizedAmount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentMethodDescription = paymentMethodDescription;
    }

    public string CurrencyCode { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>The PayPal order (checkout) id under which the hold was created.</summary>
    public string PayPalOrderId { get; private set; }

    // Authorization (the hold)
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture (money taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe description of the instrument that paid, e.g. "VISA ****1111". Never full PAN.</summary>
    public string? PaymentMethodDescription { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total amount refunded so far across all refunds.</summary>
    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>
    /// Renew an authorization that has gone stale before fulfilment. Replaces the authorization id,
    /// status and expiry with those of the fresh hold.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
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

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>Returns an already-recorded refund for the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// The amount that can still be refunded: captured minus already refunded. Zero before capture.
    /// </summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    internal void AddRefund(PaymentRefund refund)
    {
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refund.Amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount:0.00} {CurrencyCode} exceeds the refundable remaining " +
                $"{RefundableRemaining:0.00} {CurrencyCode} for this capture.");
        }
        _refunds.Add(refund);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money movement for an <see cref="Order"/>. This record carries the state PayPal owns —
/// the ids and current status of the authorization hold, the capture, and any refunds — so that a
/// later request (fulfil, cancel, refund) can act on it rather than only the request that started it.
/// It is a child of the Order aggregate; the Order is the aggregate root.
/// </summary>
public class Payment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        string currency, decimal authorizedAmount, DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>The PayPal checkout order id created to place the hold.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The PayPal authorization id for the current hold on the shopper's funds.</summary>
    public string AuthorizationId { get; private set; }

    /// <summary>The raw authorization status reported by PayPal (CREATED, CAPTURED, VOIDED, ...).</summary>
    public string AuthorizationStatus { get; private set; }

    public string Currency { get; private set; }

    public decimal AuthorizedAmount { get; private set; }

    /// <summary>When the current authorization hold expires, per PayPal.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>The amount PayPal actually captured at fulfilment.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's processing fee for the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (gross minus fee), per PayPal.</summary>
    public decimal? NetAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Replaces the current hold with a renewed authorization after a stale one has been
    /// re-authorized with PayPal (which mints a new authorization id and honor period).
    /// </summary>
    public void RecordReauthorization(string newAuthorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Cannot reauthorize a payment in status {Status}.");
        }

        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture PayPal performed at fulfilment, including fee and net proceeds.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Cannot capture a payment in status {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the authorization hold was released (voided) before any capture.</summary>
    public void Void()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Cannot void a payment in status {Status}.");
        }

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>The amount still available to refund: captured amount minus what has already been refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund against the capture. Guards that a refund can never exceed what remains
    /// refundable, and transitions the payment to partially or fully refunded accordingly.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot refund a payment in status {Status}.");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining refundable amount of {RefundableRemaining()}.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

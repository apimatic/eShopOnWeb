using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money and fulfilment state that follows an <see cref="OrderAggregate.Order"/>. This is an
/// additive aggregate: the existing order/order-item model is untouched. One payment exists per order and
/// carries enough of the state PayPal owns (the ids and current status of the hold, the capture and the
/// refunds) that a later request can act on it, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The identity of the shopper who owns this payment (and its order).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The three-character ISO-4217 currency, from configuration.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.PendingAuthorization;

    // --- PayPal-owned state ---

    /// <summary>The PayPal Orders v2 id created for this payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's current status for the authorization (e.g. CREATED, CAPTURED, VOIDED).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When PayPal's hold expires; used to decide whether an authorization must be renewed.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The PayPal capture id (created at fulfilment).</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's current status for the capture.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>The amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, if the shopper paid with one (Flow 2). Null for one-off cards.</summary>
    public int? SavedCardId { get; private set; }

    /// <summary>A safe description of the funding card, e.g. "VISA ****1111". Never full card details.</summary>
    public string? CardDescription { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    /// <summary>The sum of refunds that count against the captured amount.</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsTowardRefunded()).Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never negative.</summary>
    public decimal RefundableAmount() => Math.Max(0m, (CapturedAmount ?? 0m) - TotalRefunded());

    /// <summary>Records that a PayPal order was created for this payment (before the hold is placed).</summary>
    public void AttachPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    /// <summary>Records a successful authorization (the hold).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardDescription, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardDescription = cardDescription;
        SavedCardId = savedCardId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization after a renewal (reauthorize) of a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment, including what PayPal reported.</summary>
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
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released (cancellation before fulfilment).</summary>
    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;

    /// <summary>
    /// Adds a refund and advances the status. Guarded so an order can never become refundable beyond what
    /// was captured. Returns the recorded refund.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        var totalRefunded = TotalRefunded();
        if (totalRefunded >= (CapturedAmount ?? 0m) && totalRefunded > 0m)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (totalRefunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }

    /// <summary>Finds a prior refund made under the same idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

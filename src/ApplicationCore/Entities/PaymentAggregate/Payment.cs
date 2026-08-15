using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the payment state for a single <see cref="OrderAggregate.Order"/>: the hold
/// (authorization), the capture, and any refunds — including enough of the state PayPal owns
/// (ids and current status) that a later request can act on it, not just the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currency, string payPalOrderId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        Status = PaymentStatus.PendingAuthorization;
    }

    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper who owns the order/payment. Used for authorization scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The authorized amount — equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>PayPal's order id (the v2 Orders resource that carries the payment).</summary>
    public string PayPalOrderId { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture (the money actually taken) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total amount refunded so far across all refunds against the capture.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>Amount of the capture still available to refund.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Records a confirmed authorization (a hold placed with the processor).</summary>
    public void SetAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Updates the recorded authorization after it has been renewed (reauthorized).</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>Records that the authorization was voided and the hold released.</summary>
    public void MarkVoided()
    {
        if (Status == PaymentStatus.Captured || Status == PaymentStatus.PartiallyRefunded || Status == PaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Payment {Id} has been captured and cannot be voided.");
        }
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Records the capture (money taken) and the processor's fee breakdown.</summary>
    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
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

    /// <summary>
    /// Records a refund against the capture. Guards that a partly-refunded order never becomes
    /// refundable beyond what was captured, and returns the existing refund when the same
    /// idempotency key is replayed.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be refunded from status {Status}; only a captured payment can be refunded.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        RecomputeRefundState();
        return refund;
    }

    /// <summary>Returns an existing refund recorded under the supplied idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void RecomputeRefundState()
    {
        if (CapturedAmount is null) return;
        Status = TotalRefunded >= CapturedAmount.Value ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

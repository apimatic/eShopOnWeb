using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. There is at most one payment
/// per order (keyed by <see cref="OrderId"/>), which is what makes authorize/capture idempotent
/// in effect. It carries the state PayPal owns — the ids and current status of the hold
/// (authorization), the capture, and every refund — so a later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The buyer (identity name) this payment belongs to; used to scope access.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total that was authorized, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's checkout order id (the container for the authorization/capture).</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>PayPal's authorization id (the hold on the money).</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>When the current authorization expires; used to detect a stale hold before fulfilment.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal's capture id (set once funds are taken at fulfilment).</summary>
    public string? CaptureId { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The amount PayPal actually captured (as reported by PayPal at fulfilment).</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    /// <summary>
    /// Creates the payment record for an order in the "authorized" state — the money is held.
    /// </summary>
    public Payment(int orderId, string buyerId, string currencyCode, decimal amount,
        string payPalOrderId, string authorizationId, DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization after a stale hold was renewed (re-authorized).</summary>
    public void RenewAuthorization(string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        EnsureStatus(PaymentStatus.Authorized, "renew the authorization for");
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records that the authorization was captured at fulfilment, with PayPal's own figures.</summary>
    public void MarkCaptured(string captureId, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        EnsureStatus(PaymentStatus.Authorized, "capture");
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the authorization was voided (order cancelled before fulfilment).</summary>
    public void MarkVoided()
    {
        EnsureStatus(PaymentStatus.Authorized, "cancel");
        Status = PaymentStatus.Voided;
    }

    /// <summary>The amount already refunded against the capture (sum of every completed/pending refund).</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Finds a refund previously recorded under the same idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validates that a refund of <paramref name="amount"/> is permissible right now.
    /// A partly-refunded order must never become refundable beyond what was captured.
    /// </summary>
    public void GuardCanRefund(decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be refunded because its payment is '{Status}', not captured.");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {CurrencyCode} exceeds the {RefundableRemaining:0.00} {CurrencyCode} " +
                $"still refundable on order {OrderId} (captured {CapturedAmount:0.00}, already refunded {TotalRefunded:0.00}).");
        }
    }

    /// <summary>Records a refund returned by PayPal and advances the status.</summary>
    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsureStatus(PaymentStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {action} order {OrderId}: its payment is '{Status}', expected '{expected}'.");
        }
    }
}

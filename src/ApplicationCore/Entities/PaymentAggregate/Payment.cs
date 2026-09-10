using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <see cref="OrderAggregate.Order"/>. Carries the state PayPal owns — the ids and
/// current status of the hold (authorization), the capture, and the refunds — so that a later request
/// (fulfil, cancel, refund, reconcile) can act on it without re-deriving anything.
///
/// This is a separate aggregate root from Order: the order-item model is reused unchanged and the
/// money state lives here, referenced by <see cref="OrderId"/>.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public const string PayPalProvider = "PayPal";

    public int OrderId { get; private set; }

    /// <summary>The identity of the shopper who owns the order/payment (the token's name claim).</summary>
    public string BuyerId { get; private set; }

    public string Provider { get; private set; } = PayPalProvider;

    /// <summary>ISO-4217 currency the payment is denominated in (from configuration).</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;

    /// <summary>The unique invoice id sent to PayPal (carries a timestamp), used to reconcile transactions.</summary>
    public string? InvoiceId { get; private set; }

    // ---- PayPal-owned state: the hold ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- PayPal-owned state: the capture ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Set when the shopper paid with one of their saved cards (Flow 2), otherwise null.</summary>
    public int? SavedCardId { get; private set; }

    /// <summary>
    /// A per-payment unique root for PayPal-Request-Id idempotency keys, generated once and persisted.
    /// Operation keys are derived from it (<c>{root}-authorize</c>, <c>{root}-capture</c>, …) so retries
    /// within a payment's life reuse the same key (PayPal dedupes), while a fresh payment — even one that
    /// reuses a database id after an in-memory restart — never collides with a prior run's keys.
    /// </summary>
    public string IdempotencyRoot { get; private set; } = Guid.NewGuid().ToString("N");

    public string AuthorizeKey => $"{IdempotencyRoot}-authorize";
    public string OrderKey => $"{IdempotencyRoot}-order";
    public string CaptureKey => $"{IdempotencyRoot}-capture";
    public string CaptureRenewedKey => $"{IdempotencyRoot}-capture-renewed";
    public string ReauthorizeKey => $"{IdempotencyRoot}-reauthorize";

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
    }

    public void SetPayPalOrder(string payPalOrderId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
    }

    public void SetSavedCard(int? savedCardId) => SavedCardId = savedCardId;

    /// <summary>Records the hold PayPal placed on the funds.</summary>
    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the hold with a renewed one (after a reauthorization of a stale hold).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records that the money was taken, with the figures PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released without any money moving.</summary>
    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded — never below zero.</summary>
    public decimal RefundableAmount()
    {
        var captured = CapturedGrossAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Adds a refund, enforcing that the running total of refunds never exceeds the captured amount.
    /// </summary>
    public Refund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentDomainException(
                $"Order {OrderId} cannot be refunded because its payment is '{Status}', not captured.");
        }

        if (amount > RefundableAmount())
        {
            throw new PaymentDomainException(
                $"Refund of {amount:0.00} exceeds the refundable amount {RefundableAmount():0.00} for order {OrderId}.");
        }

        var refund = new Refund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedGrossAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. This is the record of the money
/// movement that follows a real payment: the hold placed at checkout, the capture taken at
/// fulfilment, and the refunds issued on return. It carries enough of the state PayPal owns
/// (the ids and current status of the hold, the capture and the refunds) that a later request
/// can act on it, not just the request that created it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order/payment (their identity, from the auth token).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to hold/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code the payment is denominated in (from configuration).</summary>
    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.PendingAuthorization;

    /// <summary>
    /// A stable, globally-unique token minted when the payment is created. It seeds the
    /// deterministic <c>PayPal-Request-Id</c> used to authorize this order, so a double-submit
    /// reuses the same id (PayPal treats it as a replay) while never colliding with another
    /// payment — including one that reused the same numeric order id in a later in-memory run.
    /// </summary>
    public string IdempotencyToken { get; private set; } = Guid.NewGuid().ToString("N");

    // --- State owned by PayPal ---

    /// <summary>PayPal's order id (the container that holds the authorization).</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal's authorization id (the hold on the money).</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's capture id (created when the money is actually taken).</summary>
    public string? CaptureId { get; private set; }

    /// <summary>The amount PayPal reported it captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>A safe description of the card used to pay (e.g. "VISA ****1111"); never full details.</summary>
    public string? CardDescription { get; private set; }

    /// <summary>The saved card used, when the shopper paid with one of their vaulted cards.</summary>
    public int? SavedCardId { get; private set; }

    /// <summary>When the hold was placed (UTC).</summary>
    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>When the money was taken (UTC). Used to line the capture up in a reconciliation range.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    /// <summary>Total refunded so far across every refund on this payment.</summary>
    public decimal RefundedAmount() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still refundable.</summary>
    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - RefundedAmount();

    /// <summary>Record a successful authorization (a hold placed on the money).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? cardDescription, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        CardDescription = cardDescription;
        SavedCardId = savedCardId;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization id after a stale authorization has been renewed.</summary>
    public void MarkReauthorized(string authorizationId)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Record the capture (money taken) together with what PayPal reported.</summary>
    public void MarkCaptured(string captureId, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Record that the hold was released before capture; no money moved.</summary>
    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>
    /// Add a refund against the capture. Enforces that the payment can never become refundable
    /// beyond what was captured. Idempotent on <paramref name="idempotencyKey"/>: a repeat under
    /// the same key returns the existing refund without recording another.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (amount > RefundableAmount())
        {
            throw new PaymentException(
                $"Refund of {amount} exceeds the refundable amount {RefundableAmount()} for capture {CaptureId}.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundedAmount() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    /// <summary>Find an already-recorded refund for a caller idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

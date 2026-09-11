using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment state for a single <see cref="OrderAggregate.Order"/>. This is an
/// additive aggregate: the existing <c>Order</c> is unchanged, and one <see cref="OrderPayment"/>
/// row is created per order to carry the money movement (the PayPal hold, capture and refunds)
/// plus enough of the state PayPal owns (ids and statuses) that a later request can act on it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.PendingPayment;
        PaymentReference = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// A process-unique reference minted when the payment record is created. Drives the deterministic
    /// PayPal-Request-Id (idempotency) keys and the purchase-unit custom_id/invoice_id used for
    /// reconciliation, so those values are stable across retries of the same order yet unique across
    /// runs (the in-memory store re-mints integer order ids from 1 on every restart).
    /// </summary>
    public string PaymentReference { get; private set; }

    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order/payment (JWT identity). Enforces per-shopper scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State owned by PayPal (so a later request can act on it) ---

    /// <summary>PayPal Orders v2 order id created at authorize time.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization (hold) id.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's last reported authorization status.</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization hold expires (drives stale-hold renewal at fulfil).</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id created at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's last reported capture status.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>Amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (captured minus fee), as PayPal reported.</summary>
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // --- Behaviour ---

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>A stale hold was renewed (reauthorized) before fulfilment: a new authorization id replaces the old.</summary>
    public void MarkAuthorizationRenewed(string authorizationId, string? authorizationStatus,
        DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkCaptured(string captureId, string? captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>Total refunded across all refunds recorded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>The amount still available to refund (captured minus already refunded).</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Find a refund previously produced under the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Record a refund. Guards the invariant that a partly-refunded order never becomes refundable
    /// beyond what was captured.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException(
                $"Cannot refund a payment in status '{Status}'. Only a captured payment can be refunded.");

        if (refund.Amount > RefundableRemaining())
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the remaining refundable amount of {RefundableRemaining()}.");

        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

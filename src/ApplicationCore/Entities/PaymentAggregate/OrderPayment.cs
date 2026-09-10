using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal-owned money state for a single <see cref="OrderAggregate.Order"/>.
/// Carries enough state (ids and current status for the hold, the capture and the refunds)
/// that a later request can act on it without re-deriving anything.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

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
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.UtcNow;

        // Stable-per-order but globally-unique identifiers, persisted up front so a double-click
        // reuses them (PayPal de-duplicates) while different orders/runs never collide.
        PayPalInvoiceId = NewInvoiceReference(orderId);
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    private static string NewInvoiceReference(int orderId) => $"eshop-order-{orderId}-{Guid.NewGuid():N}";

    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the shopper's username). Used for access scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    // ---- PayPal-owned state -------------------------------------------------

    /// <summary>PayPal Orders v2 order id created when authorizing.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>
    /// The unique invoice_id we stamp on the PayPal authorization/capture/refund for this order.
    /// Unique per merchant (PayPal rejects duplicates), and mapped back to the order in reconciliation.
    /// </summary>
    public string? PayPalInvoiceId { get; private set; }

    /// <summary>PayPal-Request-Id used for the authorization (idempotency; stable across a double-click).</summary>
    public string AuthorizeRequestId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>PayPal-Request-Id used for the capture (idempotency; stable across a double-click).</summary>
    public string CaptureRequestId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }

    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization expires; used to detect a stale hold before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id (the taken money).</summary>
    public string? CaptureId { get; private set; }

    public string? CaptureStatus { get; private set; }

    /// <summary>Amount PayPal reported as captured (gross).</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>Fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Set when the order was paid with one of the shopper's saved cards.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // ---- Behavior -----------------------------------------------------------

    /// <summary>
    /// Prepare a fresh attempt after a failed authorization: rotate the idempotency identifiers so
    /// PayPal treats the retry as new rather than replaying the cached failure.
    /// </summary>
    public void PrepareRetry()
    {
        PayPalInvoiceId = NewInvoiceReference(OrderId);
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the hold with a fresh authorization after the previous one went stale.</summary>
    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        // The renewed hold needs a fresh capture idempotency id so the retry capture isn't a replay.
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedGross, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>Total already refunded across all refunds recorded on this payment.</summary>
    public decimal RefundedToDate() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund (captured gross minus what has already been returned).</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? 0m) - RefundedToDate();

    /// <summary>
    /// Records a refund against the capture. Guards that a partly-refunded order can never
    /// become refundable beyond what was captured.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Order {OrderId} cannot be refunded from state {Status}; it must be captured first.");
        }
        if (refund.Amount > RefundableRemaining())
        {
            throw new PaymentStateException(
                $"Refund of {refund.Amount:0.00} exceeds the remaining refundable amount of {RefundableRemaining():0.00} for order {OrderId}.");
        }

        _refunds.Add(refund);
        Status = RefundedToDate() >= (CapturedGross ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    /// <summary>Find an existing refund previously recorded under the same idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

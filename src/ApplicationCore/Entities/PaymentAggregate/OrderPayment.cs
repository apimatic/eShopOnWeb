using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// eShop's record of the money movement for one <see cref="OrderAggregate.Order"/>: the PayPal ids and
/// statuses for the hold (authorization), the take (capture) and any refunds. Additive to the existing
/// order model — one <see cref="OrderPayment"/> per order, keyed by <see cref="OrderId"/>.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceReference = invoiceReference;
        Status = PaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop <see cref="OrderAggregate.Order"/>.Id this payment settles. Unique.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (token identity). Scopes reads/writes to the caller's own payments.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// The reference eShop stamps onto the PayPal purchase unit (invoice_id / custom_id) so PayPal's own
    /// transaction reports can be lined up against this order during reconciliation.
    /// </summary>
    public string InvoiceReference { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state needed to act on the payment later ---

    /// <summary>PayPal order id from CreateOrder.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal-reported authorization status (CREATED, CAPTURED, VOIDED, ...).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization expires (from PayPal). Used to detect a stale hold.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal-reported create time of the authorization (the clock reconciliation filters on).</summary>
    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>PayPal capture id (the take).</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal-reported capture status (COMPLETED, PARTIALLY_REFUNDED, REFUNDED, ...).</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>PayPal-reported create time of the capture (the clock reconciliation filters on).</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>Gross amount PayPal captured.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (gross minus fee).</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Human-readable reason when the payment fails / cannot proceed.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // --- behavior ---

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? authorizedAt, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        FailureReason = null;
    }

    /// <summary>Replace the held authorization with a renewed one (reauthorization).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string? captureStatus, DateTimeOffset? capturedAt,
        decimal? capturedGross, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAt = capturedAt;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        FailureReason = null;
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
    }

    /// <summary>The amount actually captured (falls back to the order amount if the breakdown was absent).</summary>
    public decimal CapturedAmount => CapturedGross ?? (Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded ? Amount : 0m);

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never negative, never above captured.</summary>
    public decimal RemainingRefundable => Math.Max(0m, CapturedAmount - TotalRefunded);

    public bool HasRefundWithKey(string idempotencyKey) =>
        _refunds.Any(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Record a refund and advance status. Caller must have validated the amount against
    /// <see cref="RemainingRefundable"/> and the key against <see cref="HasRefundWithKey"/> first.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string? payPalStatus)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        refund.SetPayPalResult(payPalRefundId, payPalStatus);
        _refunds.Add(refund);

        Status = RemainingRefundable <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        CaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

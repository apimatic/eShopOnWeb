using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement and fulfilment state for a single <see cref="OrderAggregate.Order"/>.
/// Additive to the base order model: it carries the PayPal ids and statuses (hold, capture,
/// refunds) that a later request needs, without changing the order/order-item model.
/// One <see cref="Payment"/> exists per order (enforced by a unique index on <see cref="OrderId"/>).
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.AwaitingPayment;
        PublicId = Guid.NewGuid();
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>
    /// A stable, globally-unique id for this payment, generated once at creation. All PayPal
    /// idempotency keys and the reconciliation reference derive from it, so a double-click reuses
    /// the same key (idempotent) while distinct orders — even after an in-memory restart resets the
    /// integer OrderId — never collide on PayPal's side (e.g. invoice-id uniqueness).
    /// </summary>
    public Guid PublicId { get; private set; }

    /// <summary>FK to the eShop <see cref="OrderAggregate.Order"/>. Unique.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this payment (== Order.BuyerId).</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- PayPal-owned state ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>How the funds were sourced (saved card id, or "card" for a one-off). Never a PAN.</summary>
    public string? FundingDescription { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Rotated on every state change; configured as an optimistic-concurrency token.</summary>
    public Guid ConcurrencyStamp { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // Deterministic PayPal idempotency keys (stable per payment, unique across orders/runs).
    public string AuthorizeRequestKey => $"eshop-auth-{PublicId:N}";
    public string CaptureRequestKey => $"eshop-cap-{PublicId:N}";
    public string ReauthorizeRequestKey => $"eshop-reauth-{PublicId:N}";
    public string VoidRequestKey => $"eshop-void-{PublicId:N}";

    /// <summary>Stable, unique reference sent to PayPal (custom_id/invoice_id) and matched during reconciliation.</summary>
    public string ReconciliationReference => $"ESHOP-{PublicId:N}";

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    private void Touch() => ConcurrencyStamp = Guid.NewGuid();

    /// <summary>Move to a transient "authorizing" claim before calling PayPal.</summary>
    public void BeginAuthorization(string fundingDescription)
    {
        if (Status != PaymentStatus.AwaitingPayment && Status != PaymentStatus.Failed)
            throw new InvalidOperationException($"Order {OrderId} cannot be paid from status {Status}.");
        FundingDescription = fundingDescription;
        LastError = null;
        Touch();
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        LastError = null;
        Touch();
    }

    /// <summary>Replace the authorization after a stale one is renewed (re-authorized).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        Status = PaymentStatus.Fulfilled;
        LastError = null;
        Touch();
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        LastError = reason;
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>
    /// Records a refund. Guards that cumulative refunds never exceed the captured amount.
    /// Returns the existing refund if the idempotency key was already used (no double refund).
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null) return existing;

        if (Status != PaymentStatus.Fulfilled && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Order {OrderId} cannot be refunded from status {Status}.");

        if (amount > RefundableRemaining())
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the refundable remaining {RefundableRemaining():0.00} for order {OrderId}.");

        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

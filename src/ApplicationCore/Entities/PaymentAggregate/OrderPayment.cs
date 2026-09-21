using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment record for a single <see cref="OrderAggregate.Order"/>. It carries enough of the state
/// PayPal owns — the ids and current status of the hold (authorization), the capture and the refunds —
/// that a later request can act on it, not just the one that created it.
///
/// It is a separate aggregate root (one per order, linked by <see cref="OrderId"/>) so the existing
/// Order aggregate is left untouched: this is an additive capability.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, string currency, decimal amount, string invoiceReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        InvoiceReference = invoiceReference;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The eShop order this payment belongs to.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order and this payment (their username/email).</summary>
    public string BuyerId { get; private set; }

    /// <summary>ISO-4217 currency code the payment is denominated in.</summary>
    public string Currency { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// The unique reference stamped onto the PayPal order's invoice_id/custom_id at authorization, so
    /// captures and refunds are traceable and reconciliation can line them up against this order. Assigned
    /// at order creation and globally unique (the merchant account requires a unique invoice id per
    /// transaction, and it also seeds the PayPal request ids so they never collide across restarts).
    /// </summary>
    public string InvoiceReference { get; private set; }

    // --- State PayPal owns ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>A safe, human-readable description of how the order was paid (e.g. "Visa ending 1111").</summary>
    public string? PaymentMethodDescription { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records a successful authorization (the money is held, not taken).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.AwaitingPayment && Status != PaymentStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be authorized from status {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Updates the hold after a renewal (re-authorization) — the authorization id may change.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} authorization cannot be renewed from status {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>Records the capture taken at fulfilment, with what PayPal reported it netted.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be captured from status {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    /// <summary>Records that the hold was released before fulfilment; no money moved.</summary>
    public void MarkCanceled()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be canceled from status {Status}.");
        }

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Canceled;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>Total already refunded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>An existing refund taken under this idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. Guards that a partly-refunded order never becomes refundable beyond what was
    /// captured, and advances the status to partially/fully refunded.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be refunded from status {Status}.");
        }
        if (refund.Amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable remaining of {RefundableRemaining()}.");
        }

        _refunds.Add(refund);
        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

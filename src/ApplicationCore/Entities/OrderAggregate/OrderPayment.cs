using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money-movement state that follows an <see cref="Order"/> through PayPal: the hold
/// (authorization) taken at checkout, the capture taken at fulfilment, and any refunds.
///
/// This is a distinct aggregate associated one-to-one with an <see cref="Order"/> (by
/// <see cref="OrderId"/>) so the existing order/order-item model is reused rather than
/// replaced. It carries enough of the state PayPal owns — the ids and current status of the
/// hold, the capture and the refunds — that a later request can act on it, not only the one
/// that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        // A globally-unique invoice number for this order. PayPal enforces invoice-id
        // uniqueness per merchant, so it must not repeat across app restarts (which reset the
        // in-memory order ids); the GUID guarantees that while still encoding the order id.
        InvoiceNumber = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }

    /// <summary>The unique invoice number sent to PayPal at capture.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The owning shopper. Denormalised so ownership checks and reconciliation
    /// never have to load the order.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, snapshotted from catalog prices.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // ----- PayPal Checkout order (the container the hold lives in) -----
    public string? PayPalOrderId { get; private set; }

    // ----- Authorization (the hold) -----
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- Capture (the money actually taken at fulfilment) -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Records a fresh hold. The saved-card token used (if any) is not retained here; only
    /// the PayPal-owned ids/status are.
    /// </summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records the result of renewing a stale hold before fulfilment.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records the capture taken at fulfilment, including PayPal's fee and the net
    /// proceeds to the merchant.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released before fulfilment; no money moved.</summary>
    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkFailed() => Status = PaymentStatus.Failed;

    /// <summary>The amount still refundable: captured minus refunds already taken (excluding
    /// any that failed). A partly-refunded order can never be refunded beyond what was
    /// captured.</summary>
    public decimal RefundableAmount()
    {
        if (CapturedAmount is null) return 0m;
        var refunded = _refunds
            .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);
        var remaining = CapturedAmount.Value - refunded;
        return remaining > 0m ? remaining : 0m;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }

    /// <summary>Re-derives the payment status after a refund, guarding the "never refundable
    /// beyond captured" invariant.</summary>
    public void RecomputeRefundStatus()
    {
        if (CapturedAmount is null) return;
        var remaining = RefundableAmount();
        if (remaining <= 0m)
            Status = PaymentStatus.Refunded;
        else if (remaining < CapturedAmount.Value)
            Status = PaymentStatus.PartiallyRefunded;
    }
}

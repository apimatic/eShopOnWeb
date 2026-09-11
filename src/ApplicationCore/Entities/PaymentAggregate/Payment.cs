using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money movement for a single <see cref="OrderAggregate.Order"/>. This aggregate owns the
/// PayPal-side state (the hold/authorization, the capture, and any refunds) so that a later
/// request can act on it. There is at most one Payment per order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceId = invoiceId;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment; used for shopper-scoped access checks.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total snapshot, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Unique reference sent to PayPal (invoice_id) used to reconcile transactions back to this order.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state for the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state for the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Human-readable reason the last authorization attempt failed, if any.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records that a stale authorization was renewed (reauthorized) into a fresh hold.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Total already refunded (successful or in-flight) against the capture.</summary>
    public decimal RefundedTotal() => _refunds
        .Where(r => !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? 0m) - RefundedTotal();

    /// <summary>
    /// Reserves a refund of <paramref name="amount"/> under <paramref name="idempotencyKey"/>.
    /// Returns the existing refund if the key was already used (idempotent replay), otherwise a
    /// new pending refund. Throws if the amount would exceed what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
            return existing;

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException("Only a captured payment can be refunded.");

        if (amount > RefundableRemaining())
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the refundable remaining of {RefundableRemaining():0.00}.");

        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Applies PayPal's response for a reserved refund and recomputes the payment status.</summary>
    public void ConfirmRefund(PaymentRefund refund, string payPalRefundId, string status)
    {
        refund.MarkIssued(payPalRefundId, status);
        Status = RefundedTotal() >= (CapturedGross ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    /// <summary>Removes a refund reservation that PayPal rejected, restoring the prior status.</summary>
    public void DiscardRefund(PaymentRefund refund)
    {
        _refunds.Remove(refund);
        Status = RefundedTotal() > 0m ? PaymentStatus.PartiallyRefunded : PaymentStatus.Captured;
    }
}

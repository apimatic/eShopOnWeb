using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for an order. Carries the processor-owned identifiers (PayPal order,
/// authorization and capture ids) and statuses so that any later request can act on the
/// payment, plus the capture breakdown (gross, processor fee, net) once money moved.
/// Never stores full card details.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    private OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = OrderPaymentStatus.AuthorizationPending;
        PaymentReference = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The amount of the order total that is authorized / captured.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public OrderPaymentStatus Status { get; private set; }

    /// <summary>Stable per-attempt reference used to derive processor idempotency keys.</summary>
    public string PaymentReference { get; private set; }

    // Processor-owned state for the hold.
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Processor-owned state for the capture.
    public string? PayPalCaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Safe, display-only card metadata reported by the processor.
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    /// <summary>Set when the payment was made with a saved card.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public string? LastFailureReason { get; private set; }

    /// <summary>How many times the hold has been renewed; keeps renewal idempotency keys distinct.</summary>
    public int RenewalCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public static OrderPayment CreatePending(int orderId, string buyerId, decimal amount, string currency)
    {
        return new OrderPayment(orderId, buyerId, amount, currency);
    }

    /// <summary>Starts a fresh authorization attempt (e.g. after a decline or an unrecoverable hold).</summary>
    public void BeginNewAuthorizationAttempt()
    {
        if (Status == OrderPaymentStatus.Authorized || Status == OrderPaymentStatus.Captured)
        {
            throw new PaymentStateConflictException($"Payment for order {OrderId} is already {Status} and cannot be authorized again.");
        }
        Status = OrderPaymentStatus.AuthorizationPending;
        PaymentReference = Guid.NewGuid().ToString("N");
        PayPalOrderId = null;
        PayPalAuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpiresAt = null;
        LastFailureReason = null;
        Touch();
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        SavedPaymentMethodId = savedPaymentMethodId;
        LastFailureReason = null;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void MarkAuthorizationFailed(string reason)
    {
        Status = OrderPaymentStatus.Failed;
        LastFailureReason = reason;
        Touch();
    }

    /// <summary>Records a renewed hold after the original authorization went stale.</summary>
    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateConflictException($"Payment for order {OrderId} cannot be renewed while in status {Status}.");
        }
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        RenewalCount++;
        Touch();
    }

    public void MarkCaptured(string captureId, decimal grossAmount, decimal? fee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateConflictException($"Payment for order {OrderId} cannot be captured while in status {Status}.");
        }
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PayPalCaptureId = captureId;
        CapturedAmount = grossAmount;
        PayPalFee = fee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = OrderPaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateConflictException($"Payment for order {OrderId} cannot be voided while in status {Status}.");
        }
        Status = OrderPaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    /// <summary>Amount of the capture that has already been refunded (excluding failed refunds).</summary>
    public decimal TotalRefunded()
    {
        return _refunds.Where(r => r.Status != PaymentRefund.RefundStatusFailed).Sum(r => r.Amount);
    }

    /// <summary>Amount of the capture that can still be refunded.</summary>
    public decimal RemainingRefundable()
    {
        if (Status != OrderPaymentStatus.Captured || !CapturedAmount.HasValue)
        {
            return 0m;
        }
        var remaining = CapturedAmount.Value - TotalRefunded();
        return remaining > 0 ? remaining : 0m;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, string refundStatus, decimal amount, string? note)
    {
        if (Status != OrderPaymentStatus.Captured)
        {
            throw new PaymentStateConflictException($"Payment for order {OrderId} is not captured; nothing can be refunded.");
        }
        var refund = new PaymentRefund(Id, idempotencyKey, payPalRefundId, refundStatus, amount, Currency, note);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

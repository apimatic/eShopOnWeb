using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal payment for an order. Carries every piece of state PayPal owns
/// (order id, authorization id/status/expiry, capture id and amounts, refunds)
/// so that any later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency,
        string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, int? savedCardId, string? cardBrand, string? cardLast4)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        SavedCardId = savedCardId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    // PayPal-owned state for the hold
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // PayPal-owned state for the capture
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Safe description of the card that was used (never full card details)
    public int? SavedCardId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount =>
        Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            ? (CapturedAmount ?? 0m) - TotalRefunded
            : 0m;

    /// <summary>
    /// Replaces the hold with a renewed PayPal authorization (new id, new honor period).
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Only an authorized payment can be renewed. Current status: {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>
    /// Marks the hold as unrenewable; the order must be paid again before it can be fulfilled.
    /// </summary>
    public void MarkAuthorizationExpired()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Only an authorized payment can expire. Current status: {Status}.");
        }
        Status = PaymentStatus.AuthorizationExpired;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Only an authorized payment can be captured. Current status: {Status}.");
        }

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Only an authorized payment can be voided. Current status: {Status}.");
        }
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string idempotencyKey, string refundStatus)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Only a captured payment can be refunded. Current status: {Status}.");
        }
        if (amount > RefundableAmount)
        {
            throw new PaymentConflictException(
                $"Refund of {amount:0.00} {Currency} exceeds the remaining refundable amount of {RefundableAmount:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, amount, Currency, idempotencyKey, refundStatus);
        _refunds.Add(refund);
        Status = RefundableAmount == 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

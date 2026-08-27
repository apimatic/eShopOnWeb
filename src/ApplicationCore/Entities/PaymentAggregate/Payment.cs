using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the money movement for an Order: the PayPal authorization (hold),
/// the capture at fulfilment, and any refunds after fulfilment.
/// Carries the PayPal-owned identifiers and statuses so any later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.PendingAuthorization;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // PayPal authorization (hold) state
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // PayPal capture state (what PayPal reported at capture time)
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Set when the payment was made with a saved card
    public int? SavedCardId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount => Status == PaymentStatus.Captured
        ? (CapturedAmount ?? 0m) - TotalRefunded
        : 0m;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedCardId = savedCardId;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
            throw new PaymentConflictException($"Payment {Id} is '{Status}' and its authorization cannot be renewed.");

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
            throw new PaymentConflictException($"Payment {Id} is '{Status}' and cannot be captured.");

        CaptureId = captureId;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.PendingAuthorization)
            throw new PaymentConflictException($"Payment {Id} is '{Status}' and cannot be voided.");

        Status = PaymentStatus.Voided;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string idempotencyKey, string refundStatus)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (Status != PaymentStatus.Captured)
            throw new PaymentConflictException($"Payment {Id} is '{Status}' and cannot be refunded.");
        if (amount > RefundableAmount)
            throw new PaymentConflictException(
                $"Refund of {amount} {Currency} exceeds the refundable amount of {RefundableAmount} {Currency} on payment {Id}.");

        var refund = new PaymentRefund(payPalRefundId, amount, idempotencyKey, refundStatus);
        _refunds.Add(refund);
        return refund;
    }
}

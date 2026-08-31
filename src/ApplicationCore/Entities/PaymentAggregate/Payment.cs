using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state of the money movement for an order so that any
/// later request (fulfil, cancel, refund, reconciliation) can act on it.
/// Full card details are never stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount => CapturedAmount.HasValue
        ? Math.Max(0m, CapturedAmount.Value - TotalRefunded)
        : 0m;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Payment {Id} cannot be renewed while in state {Status}.");
        }

        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Payment {Id} cannot be captured while in state {Status}.");
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
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.Pending)
        {
            throw new PaymentStateException($"Payment {Id} cannot be voided while in state {Status}.");
        }

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string refundStatus)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Payment {Id} cannot be refunded while in state {Status}.");
        }
        if (amount > RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund of {amount} {Currency} exceeds the refundable remainder of {RefundableAmount} {Currency} on payment {Id}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, idempotencyKey, amount, refundStatus);
        _refunds.Add(refund);
        Status = RefundableAmount == 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

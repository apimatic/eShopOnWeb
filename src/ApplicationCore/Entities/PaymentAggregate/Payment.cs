using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal payment for an order: the authorization (hold), the capture
/// (money taken at fulfilment) and any refunds. Carries the PayPal-owned ids and
/// statuses so any later request can act on the payment.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingAuthorization;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>
    /// Number of PayPal orders created for this payment. Used to build distinct
    /// PayPal-Request-Id values per attempt so retries are safe but never collapsed
    /// into a previous attempt by PayPal's idempotency handling.
    /// </summary>
    public int AuthorizationAttempts { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationAttempts++;
        Status = PaymentStatus.Authorized;
        FailureReason = null;
        Touch();
    }

    public void MarkAuthorizationFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        Touch();
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Payment for order {OrderId} cannot be voided while in state {Status}.");
        }
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public PaymentRefund RegisterRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string refundStatus)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund of {amount} {currency} exceeds the refundable amount of {RefundableAmount} {currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, currency, refundStatus);
        _refunds.Add(refund);
        RefundedAmount += amount;
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

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the lifecycle of the PayPal payment for an order: the authorization (hold),
/// the capture (money taken at fulfilment) and any refunds.
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
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>
    /// Globally unique reference for this payment attempt. Used to build the
    /// PayPal-Request-Id idempotency keys so retries never duplicate an operation,
    /// even across database resets.
    /// </summary>
    public string Reference { get; private set; } = Guid.NewGuid().ToString("N");
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; } = DateTimeOffset.UtcNow;

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefund.StatusFailed)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Pending && Status != PaymentStatus.Declined)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be authorized from status {Status}.");
        }
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationExpired()
    {
        Status = PaymentStatus.AuthorizationExpired;
    }

    public void MarkDeclined(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.Declined;
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.AuthorizationExpired)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be voided from status {Status}.");
        }
        Status = PaymentStatus.Voided;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Payment {Id} cannot be captured from status {Status}.");
        }
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string currency, string? note)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Payment {Id} is not refundable from status {Status}.");
        }
        if (amount <= 0m || amount > RefundableAmount)
        {
            throw new InvalidOperationException(
                $"Refund amount {amount} exceeds the refundable balance {RefundableAmount} for payment {Id}.");
        }

        var refund = new PaymentRefund(Id, idempotencyKey, amount, currency, note);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplyRefund(PaymentRefund refund, string payPalRefundId, string refundStatus)
    {
        refund.MarkCompleted(payPalRefundId, refundStatus);
        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

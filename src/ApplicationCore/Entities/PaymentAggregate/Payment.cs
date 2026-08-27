using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state for an order's payment: the order id, the authorization
/// (hold), the capture, and any refunds, so later requests can act on them.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AuthorizationPending;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal authorizedAmount, string currency)
    {
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkAuthorizationRenewed(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Payment {Id} cannot be voided while in status {Status}.");
        }
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Payment {Id} cannot be captured while in status {Status}.");
        }
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public decimal RemainingRefundableAmount =>
        (CapturedAmount ?? 0m) - RefundedAmount;

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Payment {Id} cannot be refunded while in status {Status}.");
        }
        if (amount > RemainingRefundableAmount)
        {
            throw new PaymentConflictException(
                $"Refund of {amount} {Currency} exceeds the remaining refundable amount of {RemainingRefundableAmount} {Currency} on payment {Id}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RemainingRefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

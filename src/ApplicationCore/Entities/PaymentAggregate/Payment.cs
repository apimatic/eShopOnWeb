using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the money movement for an Order: the PayPal order, the authorization (hold),
/// the capture (at fulfilment) and any refunds. Only PayPal-owned identifiers and statuses
/// are stored here; card details never are.
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
        PaymentKey = Guid.NewGuid();
    }

    /// <summary>
    /// Globally unique key for this payment, used to derive processor idempotency keys.
    /// Survives database reseeds (unlike the row id), so a retried operation replays
    /// safely while a new payment never collides with an old one at the processor.
    /// </summary>
    public Guid PaymentKey { get; private set; }

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
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefund.RefundStatusFailed)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetPayPalOrderId(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId ??= payPalOrderId;
    }

    public void RecordAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the PayPal-reported authorization state without changing payment status.</summary>
    public void UpdateAuthorizationStatus(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Payment {Id} cannot be captured while in state {Status}.");
        }
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid(string authorizationStatus)
    {
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.PendingAuthorization)
        {
            throw new PaymentStateException($"Payment {Id} cannot be voided while in state {Status}.");
        }
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
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
                $"Refund of {amount} {Currency} exceeds the refundable remainder {RefundableAmount} {Currency} of capture {CaptureId}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, idempotencyKey, amount, Currency, status);
        _refunds.Add(refund);
        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        CaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}

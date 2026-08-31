using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    PendingAuthorization = 0,
    Authorized = 1,
    AuthorizationFailed = 2,
    Voided = 3,
    Captured = 4
}

/// <summary>
/// Tracks the PayPal-owned state of the money movement for an order:
/// the authorization (hold), the capture, and any refunds.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() {}

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency, string paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.PendingAuthorization;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string PaymentMethodDescription { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? CaptureFee { get; private set; }
    public decimal? CaptureNetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.AuthorizationFailed;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal fee, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        CaptureFee = fee;
        CaptureNetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

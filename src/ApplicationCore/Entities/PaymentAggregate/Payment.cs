using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal payment attached to an order. Carries every identifier and status
/// PayPal owns (order, authorization, capture, refunds) so any later request can
/// act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? authorizationExpiresAt, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Authorized;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string PayPalOrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }

    public string? CaptureId { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? CaptureFee { get; private set; }
    public decimal? CaptureNetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - RefundedAmount;

    public bool AuthorizationIsStale =>
        Status == PaymentStatus.Authorized &&
        (AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow);

    public void MarkAuthorizationFailed(string status)
    {
        AuthorizationStatus = status;
        Status = PaymentStatus.AuthorizationFailed;
    }

    public void MarkVoided(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
    }

    public void MarkReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationCount++;
    }

    public void MarkCaptured(string captureId, decimal grossAmount, decimal? fee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = grossAmount;
        CaptureFee = fee;
        CaptureNetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string refundStatus, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(payPalRefundId, amount, refundStatus, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RemainingRefundable <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

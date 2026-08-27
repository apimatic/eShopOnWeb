using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal-owned state (order, authorization, capture, refunds) for an eShop order
/// so that any later request can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = amount;
        Currency = currency;
        Status = PaymentStatus.None;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public PaymentStatus Status { get; private set; }

    // PayPal order / authorization (the hold)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    // Idempotency keys sent to PayPal as PayPal-Request-Id, so a retry never moves money twice
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }

    // Capture (the money actually taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset? expirationTime, string authorizeRequestId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpirationTime = expirationTime;
        AuthorizeRequestId = authorizeRequestId;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationExpired()
    {
        Status = PaymentStatus.AuthorizationExpired;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount, string captureRequestId)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CaptureRequestId = captureRequestId;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

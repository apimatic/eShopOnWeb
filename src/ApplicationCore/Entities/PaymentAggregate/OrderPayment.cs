using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state of the payment for an order (ids and current status
/// for the authorization hold, the capture and any refunds) so that later requests can
/// act on it. Never carries card data.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency, string payPalOrderId, string requestKey)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(requestKey, nameof(requestKey));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        Status = PaymentStatus.AuthorizationPending;
        CreatedAt = DateTimeOffset.UtcNow;
        RequestKey = requestKey;
    }

    /// <summary>
    /// Unique-per-payment seed for the gateway idempotency keys (PayPal-Request-Id).
    /// Keys derived from it are stable across retries of the same logical operation and
    /// never collide with keys from other payments or earlier runs.
    /// </summary>
    public string RequestKey { get; private set; }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey, string? note)
    {
        var refund = new PaymentRefund(Id, payPalRefundId, amount, status, idempotencyKey, note);
        _refunds.Add(refund);
        return refund;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - TotalRefunded();
}

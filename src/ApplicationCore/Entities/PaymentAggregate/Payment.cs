using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the state PayPal owns for one eShop order: the authorization (hold),
/// the capture, and any refunds, so later requests can act on the payment.
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
        AuthorizedAmount = amount;
        Currency = currency;
        Status = PaymentStatus.AuthorizationPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Number of authorization attempts made for this payment; used to build
    // deterministic PayPal-Request-Id values so retries stay idempotent.
    public int AuthorizationAttempts { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != "DECLINED" && r.Status != "FAILED" && r.Status != "CANCELLED")
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        AuthorizationAttempts++;
    }

    public void MarkReauthorized(string authorizationId, string authorizationStatus,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed()
    {
        Status = PaymentStatus.AuthorizationFailed;
    }

    public void MarkAuthorizationExpired()
    {
        AuthorizationStatus = "EXPIRED";
        Status = PaymentStatus.AuthorizationExpired;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(Id, payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

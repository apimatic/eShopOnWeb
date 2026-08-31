using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state of the money movement for an order so that
/// later requests (fulfil, cancel, refund) can act on it.
/// Full card details are never stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string payPalOrderId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        PayPalOrderId = payPalOrderId;
        AuthorizedAmount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    public string PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => AuthorizationId != null && VoidedAt == null && CaptureId == null;
    public bool IsCaptured => CaptureId != null;
    public bool IsVoided => VoidedAt != null;

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == PaymentRefundStatus.Completed)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => IsCaptured ? (CapturedAmount ?? 0m) - TotalRefunded : 0m;

    public void MarkAuthorized(string authorizationId, string status, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void UpdateAuthorizationState(string authorizationId, string status, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void MarkCaptured(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void MarkVoided()
    {
        VoidedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "VOIDED";
    }

    public PaymentRefund AddRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (amount > RefundableAmount)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable balance of {RefundableAmount:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(payPalRefundId, status, amount, Currency, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

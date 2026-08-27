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

    public Payment(int orderId, string buyerId, decimal orderTotal, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(orderTotal, nameof(orderTotal));

        OrderId = orderId;
        BuyerId = buyerId;
        OrderTotal = orderTotal;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal OrderTotal { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    // PayPal order / authorization (the hold)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // PayPal capture (the money actually taken)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == PaymentRefund.RefundStatusCompleted)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(Id, payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

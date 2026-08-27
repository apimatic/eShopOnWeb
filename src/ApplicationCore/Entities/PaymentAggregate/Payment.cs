using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the state PayPal owns for the payment of a single order:
/// the authorization (hold), the capture, and any refunds.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency, decimal authorizedAmount,
        string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset authorizedAt)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }

    public decimal AuthorizedAmount { get; private set; }
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsCaptured => CaptureId != null;

    public void RenewAuthorization(string newAuthorizationId, string status, DateTimeOffset authorizedAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
    }

    public void SetCapture(string captureId, string status, decimal grossAmount, decimal feeAmount, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = grossAmount;
        PayPalFee = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        VoidedAt = DateTimeOffset.UtcNow;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, payPalRefundId, status, amount);
        _refunds.Add(refund);
        return refund;
    }
}

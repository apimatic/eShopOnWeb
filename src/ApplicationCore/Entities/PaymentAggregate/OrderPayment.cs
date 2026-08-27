using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal-owned state of the payment for an eShop order: the order resource,
/// the authorization (hold), the capture taken at fulfilment, and any refunds.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() {}

    public OrderPayment(int orderId, string buyerId, string payPalOrderId, string authorizationId,
        string authorizationStatus, decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpirationTime)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpirationTime = authorizationExpirationTime;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // PayPal order + authorization (the hold)
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    // PayPal capture (taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsCaptured => CaptureId != null;

    public decimal RefundedAmount => _refunds
        .Where(r => r.Status != PaymentRefundStatus.Failed && r.Status != PaymentRefundStatus.Cancelled)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expirationTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpirationTime = expirationTime;
    }

    public void MarkAuthorizationVoided(string status)
    {
        AuthorizationStatus = status;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void SetCaptureStatus(string captureStatus)
    {
        CaptureStatus = captureStatus;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }
}

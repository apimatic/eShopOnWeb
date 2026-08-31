using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(string currency, decimal amount)
    {
        Currency = currency;
        Amount = amount;
        ExternalReference = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string ExternalReference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLastFour { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt,
        string? cardBrand,
        string? cardLastFour)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastFour = cardLastFour;
        Status = authorizationStatus == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.Pending;
    }

    public int PrepareAuthorizationAttempt()
    {
        if (AuthorizationAttempt == 0 || AuthorizationStatus is not null && AuthorizationStatus != "CREATED")
        {
            AuthorizationAttempt++;
        }
        return AuthorizationAttempt;
    }

    public void RequireNewAuthorization(string reason)
    {
        AuthorizationStatus = reason;
        Status = PaymentStatus.AwaitingPayment;
    }

    public void UpdateAuthorization(
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        Status = captureStatus == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.Pending;
    }

    public void RecordVoid(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(
        string idempotencyKey,
        string payPalRefundId,
        string payPalStatus,
        decimal amount,
        DateTimeOffset createdAt)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, payPalStatus, amount, Currency, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        CaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

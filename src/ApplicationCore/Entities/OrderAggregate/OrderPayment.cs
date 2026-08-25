using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    public int OrderId { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string AuthorizationRequestId { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string CurrencyCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<OrderPaymentRefund> _refunds = new();
    public IReadOnlyCollection<OrderPaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string authorizationRequestId)
    {
        OrderId = orderId;
        CurrencyCode = string.Empty;
        AuthorizationRequestId = authorizationRequestId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorization(string? payPalOrderId, string authorizationId, string status, decimal amount, string currencyCode, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        CurrencyCode = currencyCode;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal grossAmount, decimal feeAmount, decimal netAmount)
    {
        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = grossAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }

    public OrderPaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.OutOfRange(amount, nameof(amount), 0.01m, decimal.MaxValue);
        var refund = new OrderPaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public OrderPaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(int orderId, string currency, decimal amount)
    {
        OrderId = orderId;
        Currency = currency;
        Amount = amount;
        ExternalReference = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount => _refunds.Sum(x => x.Amount);
    public int? SavedPaymentMethodId { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string paypalOrderId, string authorizationId, string status,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee,
        decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status) => AuthorizationStatus = status;

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }
}

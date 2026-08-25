using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentRecord() { }
#pragma warning restore CS8618

    public PaymentRecord(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Status = PaymentStatus.PendingPayment;
        PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CapturedAmount { get; private set; }
    public string? CapturedCurrency { get; private set; }
    public string? PayPalFee { get; private set; }
    public string? NetAmount { get; private set; }
    public string? PaymentIdempotencyKey { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, string idempotencyKey)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        PaymentIdempotencyKey ??= idempotencyKey;
        Status = PaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetCaptured(string captureId, string captureStatus, string capturedAmount, string currency, string? fee, string? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        CapturedCurrency = currency;
        PayPalFee = fee;
        NetAmount = net;
        Status = PaymentStatus.Captured;
    }

    public void SetVoided()
    {
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string? refundId, string? refundStatus, string? amount, string? currency, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, refundStatus, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        UpdateRefundStatus();
        return refund;
    }

    public bool HasRefundWithKey(string idempotencyKey)
        => _refunds.Any(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund? GetRefundByKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public decimal RefundedTotal()
        => _refunds.Sum(r => decimal.TryParse(r.Amount, out var a) ? a : 0m);

    private void UpdateRefundStatus()
    {
        var captured = decimal.TryParse(CapturedAmount, out var cap) ? cap : 0m;
        var totalRefunded = RefundedTotal();
        Status = totalRefunded >= captured && captured > 0 ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

public static class PaymentStatus
{
    public const string PendingPayment = "PendingPayment";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Voided = "Voided";
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";
}

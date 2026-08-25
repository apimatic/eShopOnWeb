using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string currency, string payPalOrderId)
    {
        OrderId = orderId;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public string PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiry { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<RefundRecord> _refunds = new();
    public IReadOnlyCollection<RefundRecord> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds) total += r.Amount;
        return total;
    }

    public void SetAuthorization(string authId, string status, DateTimeOffset expiry)
    {
        AuthorizationId = authId;
        AuthorizationStatus = status;
        AuthorizationExpiry = expiry;
    }

    public void UpdateAuthorizationStatus(string status) => AuthorizationStatus = status;

    public void UpdateAuthorizationExpiry(DateTimeOffset expiry) => AuthorizationExpiry = expiry;

    public void SetCapture(string captureId, decimal capturedAmount, decimal fee, decimal net)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
    }

    public RefundRecord AddRefund(string refundId, string idempotencyKey, decimal amount, string status)
    {
        var record = new RefundRecord(refundId, idempotencyKey, amount, status);
        _refunds.Add(record);
        return record;
    }

    public RefundRecord? FindRefundByKey(string idempotencyKey)
    {
        foreach (var r in _refunds)
            if (r.IdempotencyKey == idempotencyKey) return r;
        return null;
    }
}

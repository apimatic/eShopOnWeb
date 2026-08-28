using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    internal OrderPayment(decimal orderAmount)
    {
        OrderAmount = orderAmount;
    }

    public decimal OrderAmount { get; private set; }
    public string? PayPalOrderId { get; private set; }
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

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    internal void SetPayPalOrder(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    internal void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void SetAuthorizationStatus(string status) => AuthorizationStatus = status;

    internal void RecordCapture(string captureId, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset? capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    internal OrderRefund AddRefund(string idempotencyKey, string payPalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            existing.Update(status, amount);
            return existing;
        }

        var refund = new OrderRefund(idempotencyKey, payPalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        return refund;
    }

    internal void UpdateRefund(string idempotencyKey, string status, decimal amount)
    {
        var refund = _refunds.Single(x => x.IdempotencyKey == idempotencyKey);
        refund.Update(status, amount);
    }

    public decimal RefundedAmount() => _refunds
        .Where(x => !string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount);
}

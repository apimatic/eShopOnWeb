using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationUpdatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string id, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? updatedAt, DateTimeOffset? expiresAt,
        int? paymentMethodId)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt ??= createdAt;
        AuthorizationUpdatedAt = updatedAt ?? createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        PayPalOrderStatus = "COMPLETED";
    }

    public void AdvanceAuthorizationAttempt()
    {
        AuthorizationAttempt++;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee, decimal? net,
        DateTimeOffset? capturedAt)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        CapturedAt = capturedAt;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }

    public PaymentRefund AddRefund(string paypalRefundId, string idempotencyKeyHash,
        string status, decimal amount, DateTimeOffset createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKeyHash == idempotencyKeyHash);
        if (existing is not null) return existing;

        var refund = new PaymentRefund(Id, paypalRefundId, idempotencyKeyHash, status, amount, createdAt);
        _refunds.Add(refund);
        if (status is "COMPLETED" or "PENDING")
        {
            RefundedAmount += amount;
            CaptureStatus = RefundedAmount == CapturedAmount ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        return refund;
    }
}

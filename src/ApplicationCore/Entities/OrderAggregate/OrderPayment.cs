using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    internal OrderPayment(int orderId, decimal amount, string currency)
    {
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        InvoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
        Status = PaymentStatus.AwaitingAuthorization;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string InvoiceId { get; private set; } = null!;
    public PaymentStatus Status { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds.Where(x => x.Status is "COMPLETED" or "PENDING").Sum(x => x.Amount);

    public void SetPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void SetAuthorization(string id, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void SetAuthorizationStatus(string status, DateTimeOffset? expiresAt = null)
    {
        AuthorizationStatus = status;
        if (expiresAt.HasValue) AuthorizationExpiresAt = expiresAt;
        if (status == "VOIDED") Status = PaymentStatus.Voided;
    }

    public void SetCapture(string id, string status, decimal amount, decimal? fee, decimal? net, DateTimeOffset? capturedAt)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        Status = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount, DateTimeOffset? createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;

        var refund = new PaymentRefund(Id, idempotencyKey, payPalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        UpdateRefundStatus();
        return refund;
    }

    public void UpdateRefundStatus()
    {
        var refunded = RefundedAmount;
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (refunded > 0)
        {
            Status = PaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }
}

public enum PaymentStatus
{
    AwaitingAuthorization,
    AuthorizationPending,
    Authorized,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided
}

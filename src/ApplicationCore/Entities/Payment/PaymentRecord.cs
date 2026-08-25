using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public class PaymentRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentRecord() { }
#pragma warning restore CS8618

    public PaymentRecord(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
        Status = PaymentRecordStatus.AwaitingPayment;
        UpdatedAt = DateTimeOffset.UtcNow;
        _refunds = new List<RefundRecord>();
        IdempotencyBase = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    // Unique GUID base used to derive PayPal idempotency keys; prevents collisions on DB reset.
    public string IdempotencyBase { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal CapturedFee { get; private set; }
    public decimal CapturedNet { get; private set; }
    public decimal TotalRefundedAmount { get; private set; }
    public PaymentRecordStatus Status { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<RefundRecord> _refunds = new();
    public IReadOnlyCollection<RefundRecord> Refunds => _refunds.AsReadOnly();

    public void SetPayPalOrderId(string payPalOrderId)
    {
        PayPalOrderId = payPalOrderId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetAuthorized(string authorizationId)
    {
        AuthorizationId = authorizationId;
        Status = PaymentRecordStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCaptured(string captureId, decimal grossAmount, decimal feeAmount, decimal netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = grossAmount;
        CapturedFee = feeAmount;
        CapturedNet = netAmount;
        Status = PaymentRecordStatus.Fulfilled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCancelled()
    {
        Status = PaymentRecordStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool CanRefund(decimal requestedAmount)
    {
        return TotalRefundedAmount + requestedAmount <= CapturedAmount;
    }

    public RefundRecord AddRefund(string payPalRefundId, decimal amount, string idempotencyKey)
    {
        TotalRefundedAmount += amount;
        Status = TotalRefundedAmount >= CapturedAmount
            ? PaymentRecordStatus.Refunded
            : PaymentRecordStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;

        var refund = new RefundRecord(Id, payPalRefundId, amount, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public RefundRecord? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var r in _refunds)
            if (r.IdempotencyKey == idempotencyKey)
                return r;
        return null;
    }
}

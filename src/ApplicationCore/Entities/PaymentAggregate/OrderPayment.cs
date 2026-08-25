using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() {}
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = OrderPaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        AuthIdempotencyKey = Guid.NewGuid().ToString();
        CaptureIdempotencyKey = Guid.NewGuid().ToString();
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderPaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string AuthIdempotencyKey { get; private set; } = "";
    public string CaptureIdempotencyKey { get; private set; } = "";

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiry { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public string? CaptureId { get; private set; }

    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);
    public decimal RefundableAmount => (CapturedAmount ?? 0) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId,
        DateTimeOffset expiry, DateTimeOffset authCreatedAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiry = expiry;
        AuthorizationCreatedAt = authCreatedAt;
        Status = OrderPaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string newAuthorizationId, DateTimeOffset newExpiry)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationExpiry = newExpiry;
        AuthorizationCreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal fee, decimal netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = netAmount;
        Status = OrderPaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        Status = OrderPaymentStatus.Cancelled;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedAmount ?? 0)
            ? OrderPaymentStatus.FullyRefunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderPayment() { }

    public OrderPayment(
        int orderId,
        string buyerId,
        string payPalOrderId,
        string authorizationId,
        decimal orderTotal,
        string currency,
        string createIdempotencyKey,
        string authorizeIdempotencyKey)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        OrderTotal = orderTotal;
        Currency = currency;
        CreateIdempotencyKey = createIdempotencyKey;
        AuthorizeIdempotencyKey = authorizeIdempotencyKey;
        Status = OrderPaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal OrderTotal { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string CreateIdempotencyKey { get; private set; }
    public string AuthorizeIdempotencyKey { get; private set; }
    public string? CaptureIdempotencyKey { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void SetCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount, string captureIdempotencyKey)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CaptureIdempotencyKey = captureIdempotencyKey;
        Status = OrderPaymentStatus.Captured;
    }

    public void SetVoided()
    {
        Status = OrderPaymentStatus.Voided;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
    }

    public void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
        var totalRefunded = 0m;
        foreach (var r in _refunds) totalRefunded += r.Amount;
        Status = totalRefunded >= (CapturedAmount ?? 0m)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds) total += r.Amount;
        return total;
    }
}

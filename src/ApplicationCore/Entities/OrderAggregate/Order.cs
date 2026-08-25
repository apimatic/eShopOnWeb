using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Order() { }
#pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = PaymentStatus.PendingPayment;
        PayIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    // Payment state
    public PaymentStatus PaymentStatus { get; private set; }
    public string PayIdempotencyKey { get; private set; } = "";
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFeeAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void MarkAuthorized(string paypalOrderId, string authorizationId, DateTimeOffset expiresAt)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string newAuthorizationId, DateTimeOffset newExpiresAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));

        PayPalAuthorizationId = newAuthorizationId;
        AuthorizationExpiresAt = newExpiresAt;
    }

    public void MarkFulfilled(string captureId, decimal capturedAmount, decimal feeAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        PaymentStatus = PaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        PaymentStatus = PaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string idempotencyKey, string paypalRefundId, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));

        var refund = new OrderRefund(Id, idempotencyKey, paypalRefundId, amount);
        _refunds.Add(refund);
        RefundedAmount += amount;

        PaymentStatus = RefundedAmount >= CapturedAmount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

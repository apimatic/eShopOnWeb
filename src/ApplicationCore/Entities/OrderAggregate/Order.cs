using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    // Payment state
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public decimal TotalRefundedAmount { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, DateTimeOffset expiresAt, string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Currency = currency;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string newAuthorizationId, DateTimeOffset newExpiresAt)
    {
        PayPalAuthorizationId = newAuthorizationId;
        AuthorizationExpiresAt = newExpiresAt;
    }

    public void MarkFulfilled(string captureId, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = paypalFee;
        NetAmount = netAmount;
        PaymentStatus = PaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        PaymentStatus = PaymentStatus.Cancelled;
    }

    public void AddRefundAmount(decimal amount)
    {
        TotalRefundedAmount += amount;
        PaymentStatus = TotalRefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.FullyRefunded
            : PaymentStatus.PartiallyRefunded;
    }
}

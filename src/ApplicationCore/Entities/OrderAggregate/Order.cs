using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
        : this(buyerId, shipToAddress, items, "USD")
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderStatus.AwaitingPayment;
        Currency = currency;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    // State owned by the payment provider (PayPal), retained so later requests can act on it.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CaptureGrossAmount { get; private set; }
    public decimal? CaptureFeeAmount { get; private set; }
    public decimal? CaptureNetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RefundableAmount()
    {
        var captured = CaptureGrossAmount ?? Total();
        return captured - RefundedAmount;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        Status = OrderStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationStatus, DateTimeOffset? expirationTime)
    {
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal? grossAmount, decimal? feeAmount, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CaptureGrossAmount = grossAmount;
        CaptureFeeAmount = feeAmount;
        CaptureNetAmount = netAmount;
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        Status = OrderStatus.Cancelled;
    }

    public void ApplyRefund(OrderRefund refund)
    {
        if (!string.Equals(refund.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(refund.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            RefundedAmount += refund.Amount;
            var ceiling = CaptureGrossAmount ?? Total();
            Status = RefundedAmount >= ceiling ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        }
    }

    public bool IsAuthorizationStale()
    {
        if (string.Equals(AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(AuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return AuthorizationExpirationTime.HasValue && AuthorizationExpirationTime.Value <= DateTimeOffset.UtcNow;
    }
}
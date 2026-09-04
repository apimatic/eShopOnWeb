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
    public string PaymentStatus { get; private set; } = "AwaitingPayment";
    public string FulfilmentStatus { get; private set; } = "Pending";
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public void SetPayPalOrder(string id) => PayPalOrderId = id;
    public void SetAuthorization(string id, string status)
    {
        PayPalAuthorizationId = id;
        PaymentStatus = status;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }
    public void SetCaptured(string id, decimal amount, decimal fee)
    {
        PayPalCaptureId = id;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = amount - fee;
        PaymentStatus = "Captured";
        FulfilmentStatus = "Fulfilled";
    }
    public void SetCancelled(string status) { PaymentStatus = status; FulfilmentStatus = "Cancelled"; }
    public void AddRefund(decimal amount) { RefundedAmount += amount; PaymentStatus = RefundedAmount >= CapturedAmount ? "Refunded" : "PartiallyRefunded"; }

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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }
}

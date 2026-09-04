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
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; } = OrderFulfilmentStatus.Unfulfilled;
    public string? PaymentProviderOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetProceeds { get; private set; }

    public void SetPaymentOrder(string providerOrderId)
    {
        PaymentProviderOrderId = providerOrderId;
        PaymentStatus = OrderPaymentStatus.AwaitingAuthorization;
    }

    public void SetAuthorization(string authorizationId)
    {
        AuthorizationId = authorizationId;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void SetCaptured(string captureId, decimal amount, decimal fee, decimal net)
    {
        CaptureId = captureId;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        PaymentStatus = OrderPaymentStatus.Captured;
        FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
    }

    public void AddRefund(decimal amount)
    {
        if (amount <= 0 || RefundedAmount + amount > CapturedAmount)
            throw new InvalidOperationException("Refund exceeds the captured amount.");
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
    }

    public void Cancel()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
    }

    public enum OrderPaymentStatus { AwaitingPayment, AwaitingAuthorization, Authorized, Captured, PartiallyRefunded, Refunded, Cancelled }
    public enum OrderFulfilmentStatus { Unfulfilled, Fulfilled, Cancelled }

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

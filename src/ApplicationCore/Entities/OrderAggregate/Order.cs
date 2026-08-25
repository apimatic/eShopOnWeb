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

    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    public PaymentRecord? Payment { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void AuthorizePayment(string payPalOrderId, string authorizationId)
    {
        if (Status != OrderStatus.PendingPayment)
            throw new InvalidOperationException($"Cannot authorize payment for order in status {Status}.");
        Payment = new PaymentRecord(payPalOrderId, authorizationId);
        Status = OrderStatus.PaymentAuthorized;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        Payment!.UpdateAuthorizationId(newAuthorizationId);
    }

    public void Fulfil(string captureId, string capturedAmountValue, string currency, string? feeValue, string? netValue)
    {
        if (Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOperationException($"Cannot fulfil order in status {Status}.");
        Payment!.RecordCapture(captureId, capturedAmountValue, currency, feeValue, netValue);
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOperationException($"Cannot cancel order in status {Status}.");
        Status = OrderStatus.Cancelled;
    }

    public void RecordRefund(decimal refundedAmount)
    {
        if (Status != OrderStatus.Fulfilled)
            throw new InvalidOperationException($"Cannot refund order in status {Status}.");
        Payment!.AddRefund(refundedAmount);
        if (decimal.TryParse(Payment.CapturedAmountValue, out var captured) && Payment.TotalRefunded >= captured)
            Status = OrderStatus.Refunded;
    }
}

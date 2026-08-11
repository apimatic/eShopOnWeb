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

    // --- Payment / fulfilment state (additive to the original eShopOnWeb order) ---

    /// <summary>
    /// A globally-unique reference for this order, independent of the surrogate key. It seeds the
    /// PayPal idempotency keys for authorize/capture so a double-click never charges twice, while
    /// staying unique across process restarts (where in-memory surrogate ids would otherwise repeat).
    /// </summary>
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state. Null until the order is paid (authorized).</summary>
    public Payment? Payment { get; private set; }

    /// <summary>Records that funds were authorized (held) with PayPal for this order.</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, string currency)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        }
        Payment = new Payment(payPalOrderId, authorizationId, authorizationStatus, Total(), currency);
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Records that the held funds were captured (money taken) as part of fulfilment.</summary>
    public void RecordFulfilment(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        }
        if (Payment is null)
        {
            throw new InvalidOperationException($"Order {Id} has no payment to capture.");
        }
        Payment.RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Records that the order was cancelled before fulfilment and the hold released.</summary>
    public void RecordCancellation()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}.");
        }
        Status = OrderStatus.Cancelled;
    }

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

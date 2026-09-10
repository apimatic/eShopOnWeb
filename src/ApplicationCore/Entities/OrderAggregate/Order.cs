using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>The fulfilment/payment lifecycle state of the order.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>The PayPal-backed payment for this order, once the shopper has paid. Null while awaiting payment.</summary>
    public Payment? Payment { get; private set; }

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

    /// <summary>
    /// Records the PayPal authorization taken at pay-time, moving the order to <see cref="OrderStatus.Authorized"/>.
    /// Only valid while the order is awaiting payment.
    /// </summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, string currency)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be paid because it is {Status}.");
        }

        Payment = new Payment(payPalOrderId, authorizationId, authorizationStatus, authorizationExpiresAt, Total(), currency);
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once the held funds have been captured at PayPal.</summary>
    public void RecordFulfilment(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be fulfilled because it is {Status}.");
        }

        Payment!.MarkCaptured(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Releases the held funds and cancels the order. Only valid before fulfilment.</summary>
    public void RecordCancellation()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be cancelled because it is {Status}. Only an authorized, unfulfilled order can be cancelled.");
        }

        Payment!.MarkVoided();
        Status = OrderStatus.Cancelled;
    }
}

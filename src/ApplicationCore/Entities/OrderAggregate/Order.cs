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

    /// <summary>
    /// Creates an order that carries a payment awaiting authorization. Used by the payment API flow,
    /// where the total must be held with (and later captured from) PayPal in a specific currency.
    /// </summary>
    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
        : this(buyerId, shipToAddress, items)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Payment = new OrderPayment(Total(), currency);
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>The order's position in the pay → fulfil → refund lifecycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// The payment facet holding all PayPal-owned state. Null for legacy orders created through the
    /// storefront checkout that never went through the payment API.
    /// </summary>
    public OrderPayment? Payment { get; private set; }

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

    // --- Lifecycle transitions. Each guards the state machine so an operation can only run
    //     from a valid starting state (e.g. you cannot capture before authorizing). ---

    public void MarkAuthorized()
    {
        EnsureHasPayment();
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkAuthorized));
        }
        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        EnsureHasPayment();
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkFulfilled));
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        EnsureHasPayment();
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkCancelled));
        }
        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded(bool fullyRefunded)
    {
        EnsureHasPayment();
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkRefunded));
        }
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }

    private void EnsureHasPayment()
    {
        if (Payment is null)
        {
            throw new InvalidOperationException($"Order {Id} has no payment to act on.");
        }
    }
}

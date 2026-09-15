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

    // A stable, globally-unique token minted when the order is placed. It anchors the PayPal idempotency
    // keys for this order's payment operations, so retries (and concurrent double-clicks) of the same logical
    // action reuse one key — while distinct orders never collide, even when integer ids are reused across
    // an in-memory database reset.
    public Guid IdempotencyToken { get; private set; } = Guid.NewGuid();

    // Additive fulfilment state. A newly placed order awaits payment; the payment/operator flows
    // (see PaymentAggregate) drive it through the remaining states. Transitions are guarded so an
    // order can never, e.g., be fulfilled twice or cancelled after fulfilment.
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkPaymentAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}; it must be authorized first.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}; cancellation is only possible before fulfilment.");
        }
        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded(bool fullyRefunded)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded from status {Status}; only a fulfilled order can be refunded.");
        }
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
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

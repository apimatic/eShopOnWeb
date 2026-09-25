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

    // --- Payment / fulfilment state (additive) ---

    // A stable, per-order identifier used as the base for PayPal idempotency keys and as the
    // PayPal purchase-unit custom_id/invoice_id, so an authorization/capture/void resent after a
    // failure dedupes at PayPal, and transactions can be reconciled back to this order. Persisted
    // with the order, so it is identical across retries of the same logical order and unique across
    // orders (unlike the in-memory integer key, which restarts per process).
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>Money is held (authorized) but not yet taken. Only valid while awaiting payment.</summary>
    public void MarkAuthorized()
    {
        if (PaymentStatus is OrderPaymentStatus.Authorized) return; // idempotent
        Guard.Against.OutOfRange((int)PaymentStatus, nameof(PaymentStatus),
            (int)OrderPaymentStatus.AwaitingPayment, (int)OrderPaymentStatus.AwaitingPayment,
            "Only an order awaiting payment can be authorized.");
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Money has been captured at fulfilment.</summary>
    public void MarkPaid()
    {
        if (PaymentStatus is OrderPaymentStatus.Paid) return; // idempotent
        if (PaymentStatus is not OrderPaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be fulfilled/captured.");
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>Held funds released before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (PaymentStatus is OrderPaymentStatus.Cancelled) return; // idempotent
        if (PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.Authorized))
            throw new InvalidOperationException("Only an order that has not been captured can be cancelled.");
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Records a refund outcome against a captured order.</summary>
    public void MarkRefunded(bool fullyRefunded)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a captured (paid) order can be refunded.");
        PaymentStatus = fullyRefunded ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
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

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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    // --- Additive payment / fulfilment state (PayPal integration) ---------------------------

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// PayPal-owned payment state (hold, capture, refunds). Null until the order is paid.
    /// Mapped as an owned entity so it is always loaded with the order.
    /// </summary>
    public OrderPayment? Payment { get; private set; }

    /// <summary>
    /// Records a successful authorization (funds held) for the order total in the given currency.
    /// Idempotent transition: an already-authorized order keeps its existing payment.
    /// </summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        string currency, DateTimeOffset? expiresAt)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"Order {Id} is cancelled and cannot be paid.");
        }
        if (Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOperationException($"Order {Id} is already fulfilled.");
        }

        Payment ??= new OrderPayment(Total(), currency);
        Payment.SetAuthorization(payPalOrderId, authorizationId, authorizationStatus, expiresAt);
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Records a renewed authorization after a stale one was reauthorized at fulfilment time.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        RequirePayment().RenewAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>
    /// Records that the held funds were captured (money taken) and marks the order fulfilled.
    /// </summary>
    public void RecordFulfilment(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"Order {Id} is cancelled and cannot be fulfilled.");
        }
        RequirePayment().SetCapture(captureId, captureStatus, capturedAmount, fee, net);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Records that the authorization was voided before fulfilment and releases the hold.</summary>
    public void RecordCancellation()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOperationException($"Order {Id} is already fulfilled and cannot be cancelled; refund it instead.");
        }
        Payment?.MarkAuthorizationVoided();
        Status = OrderStatus.Cancelled;
    }

    public OrderPayment RequirePayment() =>
        Payment ?? throw new InvalidOperationException($"Order {Id} has no payment; authorize it first.");
}

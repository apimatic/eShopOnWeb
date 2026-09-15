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

    // ----- Payment / fulfilment state (additive) -----

    /// <summary>The fulfilment lifecycle of this order. Defaults to <see cref="OrderStatus.AwaitingPayment"/>.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The payment for this order, once the shopper has paid (authorized). Null while awaiting payment.</summary>
    public Payment? Payment { get; private set; }

    /// <summary>
    /// Records that the order total has been authorized (a hold placed) with PayPal. Idempotent
    /// callers should short-circuit before calling this; it guards against double authorization.
    /// </summary>
    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, decimal amount, string currency, string reconciliationReference)
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot pay for a cancelled order.");
        if (Payment is not null)
            throw new InvalidOperationException("This order has already been paid (authorized).");

        Payment = new Payment(payPalOrderId, authorizationId, authorizationStatus,
            authorizationExpiresAt, amount, currency, reconciliationReference);
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled after its held funds have been captured.</summary>
    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Cannot fulfil a cancelled order.");
        Guard.Against.Null(Payment, nameof(Payment), "Cannot fulfil an order that has not been paid.");

        Payment!.MarkCaptured(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels the order before fulfilment, releasing any held funds.</summary>
    public void Cancel()
    {
        if (Status == OrderStatus.Fulfilled)
            throw new InvalidOperationException("Cannot cancel an order that has already been fulfilled; issue a refund instead.");
        if (Status == OrderStatus.Cancelled)
            return; // idempotent

        Payment?.MarkAuthorizationVoided();
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

using System;
using System.Collections.Generic;
using System.Linq;
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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
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

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public OrderPayment EnsurePayment(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        if (Payment == null)
        {
            if (Id <= 0)
            {
                throw new InvalidOperationException("The order must be persisted before a payment can be attached.");
            }

            Payment = new OrderPayment(Id, currency);
        }

        return Payment;
    }

    public void MarkAuthorized()
    {
        EnsurePayable();
        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded()
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded from status {Status}.");
        }

        if (Payment == null)
        {
            throw new InvalidOperationException($"Order {Id} has no captured payment to refund.");
        }

        Status = Payment.RefundableRemaining <= 0.001m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }

    public void EnsureOwnedBy(string buyerId)
    {
        if (!BelongsTo(buyerId))
        {
            throw new UnauthorizedAccessException("The requested order does not belong to the caller.");
        }
    }

    public void EnsurePayable()
    {
        if (Status == OrderStatus.Authorized)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid from status {Status}.");
        }
    }

    public void EnsureCancellable()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }
    }

    public bool IsAlreadyAuthorized() =>
        Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        && Payment?.AuthorizationId != null;

    public bool IsAlreadyFulfilled() =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        && Payment?.CaptureId != null;

    public bool IsAlreadyCancelled() => Status == OrderStatus.Cancelled;

    public decimal RemainingRefundableAmount() => Payment?.RefundableRemaining ?? 0m;
}

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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
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

    public void MarkPaymentAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentDomainException($"Order {Id} cannot be marked as paid while in status {Status}.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentDomainException($"Order {Id} cannot be fulfilled while in status {Status}; it must be paid first.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentDomainException($"Order {Id} has already been fulfilled; issue a refund instead of cancelling.");
        }
        if (Status != OrderStatus.Cancelled)
        {
            Status = OrderStatus.Cancelled;
        }
    }

    public void MarkRefunded(bool partial)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentDomainException($"Order {Id} cannot be refunded while in status {Status}; it must be fulfilled first.");
        }
        Status = partial ? OrderStatus.PartiallyRefunded : OrderStatus.Refunded;
    }
}

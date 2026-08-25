using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Order() { }
#pragma warning restore CS8618

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
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public OrderPayment? Payment { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void SetPaymentAuthorized(OrderPayment payment)
    {
        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void SetFulfilled()
    {
        Status = OrderStatus.Fulfilled;
    }

    public void SetCancelled()
    {
        Status = OrderStatus.Cancelled;
    }

    public void SetRefunded(bool partial)
    {
        Status = partial ? OrderStatus.PartiallyRefunded : OrderStatus.Refunded;
    }
}

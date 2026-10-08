using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
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

    // Payment attempts and refunds are written through their own claim rows (see OrderPayment / OrderRefund);
    // the order only reads them to derive its payment state.
    private readonly List<OrderPayment> _payments = new List<OrderPayment>();
    public IReadOnlyCollection<OrderPayment> Payments => _payments.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    /// <summary>The payment attempt that took the money, if any.</summary>
    public OrderPayment? CapturedPayment() =>
        _payments.FirstOrDefault(p => p.Status == PaymentAttemptStatus.Authorised);

    /// <summary>The latest attempt that may still turn into a charge, if any.</summary>
    public OrderPayment? InFlightPayment() =>
        _payments.Where(p => p.IsInFlight).OrderByDescending(p => p.AttemptNumber).FirstOrDefault();

    /// <summary>Refunded so far, counting refunds whose outcome is not settled yet (minor units).</summary>
    public long RefundedOrClaimedMinor() =>
        _refunds.Where(r => r.CountsAgainstPayment).Sum(r => r.AmountMinor);

    /// <summary>Refunds the provider has accepted (minor units).</summary>
    public long RefundedMinor() =>
        _refunds.Where(r => r.Status == RefundStatus.Received).Sum(r => r.AmountMinor);

    /// <summary>What may still be refunded without exceeding what was paid (minor units).</summary>
    public long RefundableMinor()
    {
        var payment = CapturedPayment();
        return payment is null ? 0 : Math.Max(0, payment.AmountMinor - RefundedOrClaimedMinor());
    }

    public OrderPaymentStatus PaymentStatus()
    {
        var payment = CapturedPayment();
        if (payment is null)
        {
            return InFlightPayment() is null ? OrderPaymentStatus.AwaitingPayment : OrderPaymentStatus.PaymentPending;
        }

        var refunded = RefundedMinor();
        if (refunded == 0) return OrderPaymentStatus.Paid;
        return refunded >= payment.AmountMinor ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
    }
}

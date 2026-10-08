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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
        : this(buyerId, shipToAddress, items)
    {
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Currency = currency;
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

    /// <summary>ISO-4217 code the order is priced and charged in. Empty for orders created before payments existed.</summary>
    public string Currency { get; private set; } = string.Empty;
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public decimal AmountPaid { get; private set; }

    /// <summary>Sum of refunds submitted or in flight; reserved before the processor is called so it can never exceed <see cref="AmountPaid"/>.</summary>
    public decimal AmountRefunded { get; private set; }
    public string? PaymentPspReference { get; private set; }
    public DateTimeOffset? PaidDate { get; private set; }

    /// <summary>Optimistic-concurrency token; changes on every payment-state change.</summary>
    public Guid PaymentConcurrencyStamp { get; private set; } = Guid.NewGuid();

    public decimal RefundableAmount => AmountPaid - AmountRefunded;

    public bool IsPaid => PaymentStatus is OrderPaymentStatus.Paid
        or OrderPaymentStatus.PartiallyRefunded
        or OrderPaymentStatus.Refunded;

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    /// <summary>Sets the charge currency of an order that predates payments.</summary>
    public void AssignCurrencyIfMissing(string currency)
    {
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        if (string.IsNullOrEmpty(Currency) && !IsPaid)
        {
            Currency = currency;
            Touch();
        }
    }

    public void MarkPaymentPending()
    {
        if (IsPaid) throw new InvalidOperationException("The order is already paid.");
        PaymentStatus = OrderPaymentStatus.PaymentPending;
        Touch();
    }

    public void MarkAwaitingPayment()
    {
        if (IsPaid) throw new InvalidOperationException("The order is already paid.");
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        Touch();
    }

    public void MarkPaid(string pspReference, decimal amount, DateTimeOffset paidDate)
    {
        Guard.Against.NullOrWhiteSpace(pspReference, nameof(pspReference));
        if (IsPaid) throw new InvalidOperationException("The order is already paid.");
        if (amount != Total()) throw new InvalidOperationException("The paid amount does not match the order total.");

        PaymentPspReference = pspReference;
        AmountPaid = amount;
        PaidDate = paidDate;
        PaymentStatus = OrderPaymentStatus.Paid;
        Touch();
    }

    public void ReserveRefund(decimal amount)
    {
        if (!IsPaid) throw new InvalidOperationException("Only a paid order can be refunded.");
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableAmount) throw new InvalidOperationException("The refund exceeds the amount still refundable on this order.");

        AmountRefunded += amount;
        RecomputeRefundStatus();
    }

    public void ReleaseRefund(decimal amount)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > AmountRefunded) throw new InvalidOperationException("Cannot release more than was reserved.");

        AmountRefunded -= amount;
        RecomputeRefundStatus();
    }

    private void RecomputeRefundStatus()
    {
        PaymentStatus = AmountRefunded == 0m ? OrderPaymentStatus.Paid
            : AmountRefunded < AmountPaid ? OrderPaymentStatus.PartiallyRefunded
            : OrderPaymentStatus.Refunded;
        Touch();
    }

    private void Touch() => PaymentConcurrencyStamp = Guid.NewGuid();
}

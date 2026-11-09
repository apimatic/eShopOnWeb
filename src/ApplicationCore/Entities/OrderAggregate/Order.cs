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

    // ---- Payment state (Stax) ----
    public PaymentState PaymentState { get; private set; } = PaymentState.Unpaid;
    public string? PaymentMethodId { get; private set; }
    public string? PaymentMethodKind { get; private set; }
    public string? StaxTransactionId { get; private set; }
    public decimal Fee { get; private set; }
    public decimal AmountCharged { get; private set; }
    public int PaymentAttempts { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? PaymentNote { get; private set; }

    // ---- Refund state ----
    public RefundStatus RefundStatus { get; private set; } = RefundStatus.None;
    public DateTimeOffset? RefundRequestedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string? RefundNote { get; private set; }

    /// <summary>True when the payment has gone through and has not been returned to the shopper.</summary>
    public bool IsPaid => PaymentState == PaymentState.Paid && RefundStatus != RefundStatus.Refunded;

    public void RecordPaymentAttemptFailed(string note)
    {
        PaymentAttempts++;
        PaymentState = PaymentState.Unpaid;
        PaymentNote = note;
    }

    public void RecordPayment(string paymentMethodId, string kind, string transactionId, decimal fee, decimal amountCharged, bool settled)
    {
        PaymentAttempts++;
        PaymentMethodId = paymentMethodId;
        PaymentMethodKind = kind;
        StaxTransactionId = transactionId;
        Fee = fee;
        AmountCharged = amountCharged;
        PaymentNote = null;
        if (settled) MarkPaid(); else PaymentState = PaymentState.Pending;
    }

    public void MarkPaid()
    {
        PaymentState = PaymentState.Paid;
        PaidAt ??= DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string note)
    {
        PaymentState = PaymentState.Failed;
        PaymentNote = note;
    }

    public void RequestRefund()
    {
        RefundStatus = RefundStatus.Pending;
        RefundRequestedAt ??= DateTimeOffset.UtcNow;
    }

    public void MarkRefunded()
    {
        RefundStatus = RefundStatus.Refunded;
        RefundedAt = DateTimeOffset.UtcNow;
        RefundNote = null;
    }

    public void NoteRefund(string? note) => RefundNote = note;

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

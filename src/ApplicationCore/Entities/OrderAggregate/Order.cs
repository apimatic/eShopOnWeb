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

    // ---- Payment (Stax) -------------------------------------------------------------------

    /// <summary>Stax id of the payment method the order was paid with.</summary>
    public string? PaymentMethodId { get; private set; }

    /// <summary>"card" or "bank".</summary>
    public string? PaymentMethodType { get; private set; }

    /// <summary>Stax id of the charge transaction.</summary>
    public string? PaymentTransactionId { get; private set; }

    public PaymentState PaymentState { get; private set; } = PaymentState.Unpaid;

    /// <summary>Card processing fee passed on to the shopper.</summary>
    public decimal Fee { get; private set; }

    /// <summary>What Stax actually charged (order amount plus fee). Null until a charge was made.</summary>
    public decimal? AmountCharged { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>Number of payment attempts that reached a definite outcome (used for idempotency keys).</summary>
    public int PaymentAttempts { get; private set; }

    public string? PaymentNote { get; private set; }

    // ---- Refund ---------------------------------------------------------------------------

    public RefundState RefundState { get; private set; } = RefundState.None;
    public string? RefundTransactionId { get; private set; }
    public DateTimeOffset? RefundRequestedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string? RefundNote { get; private set; }

    public bool IsPaid => PaymentState == PaymentState.Paid;

    public void RecordDeclinedPayment(string? paymentMethodId, string? methodType, string? note)
    {
        PaymentAttempts++;
        PaymentMethodId = paymentMethodId;
        PaymentMethodType = methodType;
        PaymentNote = note;
    }

    public void RecordCharge(string paymentMethodId, string methodType, string transactionId,
        decimal amountCharged, decimal fee, bool settled)
    {
        PaymentAttempts++;
        PaymentMethodId = paymentMethodId;
        PaymentMethodType = methodType;
        PaymentTransactionId = transactionId;
        AmountCharged = amountCharged;
        Fee = fee;
        PaymentNote = null;
        if (settled)
        {
            MarkPaid();
        }
        else
        {
            PaymentState = PaymentState.Pending;
        }
    }

    public void MarkPaid()
    {
        PaymentState = PaymentState.Paid;
        PaidAt ??= DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string? note)
    {
        PaymentState = PaymentState.Failed;
        PaymentNote = note;
        if (RefundState == RefundState.Pending)
        {
            RefundState = RefundState.Cancelled;
            RefundNote = "The payment did not go through, so there is nothing to refund.";
        }
    }

    public void RequestRefund(DateTimeOffset now)
    {
        RefundState = RefundState.Pending;
        RefundRequestedAt ??= now;
        RefundNote = null;
    }

    public void MarkRefunded(string? refundTransactionId, DateTimeOffset now)
    {
        RefundState = RefundState.Refunded;
        RefundTransactionId = refundTransactionId ?? RefundTransactionId;
        RefundedAt = now;
        RefundNote = null;
    }

    public void MarkRefundFailed(string? note)
    {
        RefundState = RefundState.Failed;
        RefundNote = note;
    }

    public void SetRefundNote(string? note) => RefundNote = note;

    /// <summary>Removes the shopper's personal data but keeps the financial record.</summary>
    public void Anonymize(string anonymousBuyerId)
    {
        BuyerId = anonymousBuyerId;
        ShipToAddress = new Address(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }
}

public enum PaymentState
{
    Unpaid = 0,
    /// <summary>Submitted (e.g. a bank payment) but not yet settled.</summary>
    Pending = 1,
    Paid = 2,
    Failed = 3
}

public enum RefundState
{
    None = 0,
    Pending = 1,
    Refunded = 2,
    Failed = 3,
    Cancelled = 4
}

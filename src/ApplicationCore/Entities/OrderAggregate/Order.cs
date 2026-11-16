using System;
using System.Collections.Generic;
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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    // ---- Payment / fulfilment state owned jointly with Finix ----

    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PaymentFailureCode { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public long? AuthorizedAmountCents { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetProceedsCents { get; private set; }
    public string? CaptureState { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public long RefundedAmountCents { get; private set; }

    public long RemainingRefundableCents =>
        Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public bool IsAwaitingPayment => PaymentStatus is PaymentStatus.AwaitingPayment or PaymentStatus.PaymentFailed;
    public bool IsAuthorized => PaymentStatus == PaymentStatus.Authorized;
    public bool IsFulfilled => PaymentStatus is PaymentStatus.Fulfilled
        or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    public void MarkAuthorized(string authorizationId, string? authorizationState, long amountCents, string currency)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));

        FinixAuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAmountCents = amountCents;
        Currency = currency;
        AuthorizedAt = DateTimeOffset.UtcNow;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void MarkPaymentFailed(string? failureCode, string? failureMessage)
    {
        PaymentFailureCode = failureCode;
        PaymentFailureMessage = failureMessage;
        PaymentStatus = PaymentStatus.PaymentFailed;
    }

    public void MarkFulfilled(
        string captureTransferId,
        string? captureState,
        long capturedAmountCents,
        long feeCents,
        long netProceedsCents)
    {
        Guard.Against.NullOrEmpty(captureTransferId, nameof(captureTransferId));

        CaptureTransferId = captureTransferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetProceedsCents = netProceedsCents;
        FulfilledAt = DateTimeOffset.UtcNow;
        PaymentStatus = RefundedAmountCents > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        AuthorizationState = "CANCELED";
        CancelledAt = DateTimeOffset.UtcNow;
        PaymentStatus = PaymentStatus.Cancelled;
    }

    public void SetCurrency(string currency)
    {
        Currency = currency;
    }

    public void RecordRefund(long amountCents)
    {
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.OutOfRange(amountCents, nameof(amountCents), 1, RemainingRefundableCents);

        RefundedAmountCents += amountCents;
        PaymentStatus = RefundedAmountCents >= (CapturedAmountCents ?? 0)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}

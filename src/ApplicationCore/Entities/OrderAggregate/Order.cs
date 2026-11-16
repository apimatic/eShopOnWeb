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
    public string PaymentAttemptId { get; private set; } = Guid.NewGuid().ToString("N");

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; } = OrderFulfilmentStatus.NotFulfilled;

    public string? PaymentCurrency { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixVoidState { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixTransferState { get; private set; }
    public long AuthorizedAmountCents { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long FinixFeeCents { get; private set; }
    public long NetAmountCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public string? PaymentFailureMessage { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    public long TotalInCents() =>
        (long)decimal.Round(Total() * 100m, 0, MidpointRounding.AwayFromZero);

    public void MarkAuthorized(string authorizationId, string authorizationState, long amountCents,
        string currency, int? savedPaymentMethodId)
    {
        PaymentStatus = OrderPaymentStatus.Authorized;
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        AuthorizedAmountCents = amountCents;
        PaymentCurrency = currency;
        SavedPaymentMethodId = savedPaymentMethodId;
        PaymentFailureMessage = null;
        PaidAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptureFailed(string message)
    {
        PaymentStatus = OrderPaymentStatus.CaptureFailed;
        PaymentFailureMessage = message;
    }

    public void MarkCapturePending(string transferId, string transferState)
    {
        PaymentStatus = OrderPaymentStatus.CapturePending;
        FinixTransferId = transferId;
        FinixTransferState = transferState;
        PaymentFailureMessage = null;
    }

    public void MarkCaptured(string transferId, string transferState, long capturedAmountCents,
        long finixFeeCents, long netAmountCents)
    {
        PaymentStatus = OrderPaymentStatus.Captured;
        FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
        FinixTransferId = transferId;
        FinixTransferState = transferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetAmountCents = netAmountCents;
        PaymentFailureMessage = null;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled(string? authorizationState, string? voidState)
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            FinixAuthorizationState = authorizationState;
        }
        if (!string.IsNullOrWhiteSpace(voidState))
        {
            FinixVoidState = voidState;
        }
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string message)
    {
        PaymentStatus = OrderPaymentStatus.Failed;
        PaymentFailureMessage = message;
    }

    public void RotatePaymentAttempt()
    {
        PaymentAttemptId = Guid.NewGuid().ToString("N");
    }

    public void ApplyRefund(long amountCents)
    {
        RefundedAmountCents += amountCents;
        PaymentStatus = RefundedAmountCents >= CapturedAmountCents
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

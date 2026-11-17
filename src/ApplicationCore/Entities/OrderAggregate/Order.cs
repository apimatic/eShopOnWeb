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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PaymentCurrency { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? PendingAuthorizationIdempotencyKey { get; private set; }
    public long? AuthorizedAmountCents { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? FinixCaptureTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetProceedsCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

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

    public void MarkAuthorized(string authorizationId, string authorizationState, long amountCents, string currency, string paymentInstrumentId)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        PendingAuthorizationIdempotencyKey = null;
        AuthorizedAmountCents = amountCents;
        PaymentCurrency = currency;
        FinixPaymentInstrumentId = paymentInstrumentId;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void SetPendingAuthorizationIdempotencyKey(string idempotencyKey)
    {
        PendingAuthorizationIdempotencyKey = idempotencyKey;
    }

    public void ClearPendingAuthorizationIdempotencyKey()
    {
        PendingAuthorizationIdempotencyKey = null;
    }

    public void UpdateAuthorizationState(string authorizationState)
    {
        FinixAuthorizationState = authorizationState;
    }

    public void MarkCaptured(string transferId, string captureState, long capturedAmountCents, long feeCents, long netProceedsCents, string currency)
    {
        FinixCaptureTransferId = transferId;
        FinixCaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetProceedsCents = netProceedsCents;
        PaymentCurrency = currency;
        PaymentStatus = OrderPaymentStatus.Captured;
        FulfilledAt ??= DateTimeOffset.Now;
    }

    public void MarkCanceled(string? authorizationState)
    {
        if (authorizationState is not null)
        {
            FinixAuthorizationState = authorizationState;
        }
        PaymentStatus = OrderPaymentStatus.Canceled;
        CanceledAt = DateTimeOffset.Now;
    }

    public void RecordRefund(long amountCents)
    {
        RefundedAmountCents += amountCents;
        PaymentStatus = CapturedAmountCents is > 0 && RefundedAmountCents >= CapturedAmountCents
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

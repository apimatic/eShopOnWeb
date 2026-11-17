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
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");

    public string PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? FinixTransferId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal FeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? PaymentFailureMessage { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
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

    public void SetCurrency(string currency)
    {
        Currency = currency;
    }

    public void MarkPaymentAuthorized(string authorizationId, string authorizationState, decimal authorizedAmount, string currency, string? paymentInstrumentId)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        Currency = currency;
        CapturedAmount = 0m;
        FeeAmount = 0m;
        NetAmount = 0m;
        RefundedAmount = 0m;
        PaymentFailureMessage = null;
        PaymentStatus = OrderPaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string? failureMessage)
    {
        PaymentFailureMessage = failureMessage;
        PaymentStatus = OrderPaymentStatus.PaymentFailed;
        PaymentReference = Guid.NewGuid().ToString("N");
    }

    public void SetAuthorizationState(string authorizationState)
    {
        FinixAuthorizationState = authorizationState;
    }

    public void MarkFulfilled(string transferId, decimal capturedAmount, decimal feeAmount, decimal netAmount)
    {
        FinixTransferId = transferId;
        CapturedAmount = capturedAmount;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
        PaymentStatus = RefundedAmount > 0m ? OrderPaymentStatus.PartiallyRefunded : OrderPaymentStatus.Captured;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled()
    {
        PaymentStatus = OrderPaymentStatus.Canceled;
        CanceledAt = DateTimeOffset.UtcNow;
    }

    public void RegisterRefund(decimal amount)
    {
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount >= CapturedAmount
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

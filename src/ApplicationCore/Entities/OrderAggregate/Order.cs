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
    public PaymentState PaymentState { get; private set; } = PaymentState.AwaitingPayment;
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }
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

    public void RecordAuthorization(string paypalOrderId, string authorizationId, string status,
        string currency, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (PaymentState != PaymentState.AwaitingPayment)
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        PaymentCurrency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentState = PaymentState.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        if (PaymentState != PaymentState.Authorized)
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal fee, decimal net)
    {
        if (PaymentState != PaymentState.Authorized)
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        PaymentState = PaymentState.Captured;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void RecordCancellation(string authorizationStatus)
    {
        if (PaymentState is PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
            throw new InvalidOperationException("A captured order cannot be cancelled; refund it instead.");
        if (PaymentState == PaymentState.Cancelled) return;
        PayPalAuthorizationStatus = authorizationStatus;
        PaymentState = PaymentState.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void RecordRefund(decimal amount)
    {
        if (PaymentState is not (PaymentState.Captured or PaymentState.PartiallyRefunded))
            throw new InvalidOperationException("Only a captured order can be refunded.");
        if (!CapturedAmount.HasValue || amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");
        RefundedAmount += amount;
        PaymentState = RefundedAmount == CapturedAmount.Value ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
    }
}

public enum PaymentState
{
    AwaitingPayment,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Cancelled
}

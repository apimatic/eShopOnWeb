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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string PaymentRequestId { get; private set; } = Guid.NewGuid().ToString("N");
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? Currency { get; private set; }
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

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        string currency, DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        Currency = currency;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationCount++;
    }

    public void RecordCapture(string captureId, string status, decimal amount,
        decimal fee, decimal netProceeds, DateTimeOffset fulfilledAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = netProceeds;
        FulfilledAt = fulfilledAt;
        AuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string authorizationStatus, DateTimeOffset cancelledAt)
    {
        AuthorizationStatus = authorizationStatus;
        CancelledAt = cancelledAt;
        Status = OrderStatus.Cancelled;
    }

    public void RecordRefund(decimal amount)
    {
        RefundedAmount += amount;
        Status = RefundedAmount == CapturedAmount ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }
}

public enum OrderStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

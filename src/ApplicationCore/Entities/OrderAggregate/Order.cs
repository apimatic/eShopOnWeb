using System;
using System.Collections.Generic;
using System.Linq;
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
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public bool AuthorizationReauthorized { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

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

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void RecordAuthorization(string currency, string paypalOrderId, string paypalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt, bool reauthorized = false)
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized while it is {Status}.");
        }

        PaymentCurrency = currency;
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationReauthorized = reauthorized;
        Status = OrderStatus.Authorized;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? paypalFee, decimal? netProceeds)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled while it is {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation(string authorizationStatus)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled while it is {Status}.");
        }

        AuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string paypalRefundId, string idempotencyKey,
        string status, decimal amount)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded while it is {Status}.");
        }

        if (amount <= 0 || CapturedAmount is null || _refunds.Sum(x => x.Amount) + amount > CapturedAmount)
        {
            throw new InvalidOperationException($"Refund would exceed the captured amount for order {Id}.");
        }
        if (_refunds.Any(x => x.IdempotencyKey == idempotencyKey))
        {
            throw new InvalidOperationException($"Refund key {idempotencyKey} was already used for order {Id}.");
        }

        var refund = new PaymentRefund(paypalRefundId, idempotencyKey, status, amount);
        _refunds.Add(refund);
        var refunded = _refunds.Sum(x => x.Amount);
        Status = refunded >= CapturedAmount ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        CaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

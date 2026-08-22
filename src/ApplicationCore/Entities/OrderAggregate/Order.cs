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
        Status = OrderStatus.AwaitingPayment;
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderPayment Payment { get; private set; } = new OrderPayment();

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void RecordPayPalOrder(string paypalOrderId, string? status, string currency, string authorizeRequestId)
    {
        Payment.RecordPayPalOrder(paypalOrderId, status, currency, authorizeRequestId);
    }

    public void MarkAuthorized(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration, string? paypalOrderStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");

        Payment.RecordAuthorization(authorizationId, authorizationStatus, expiration, paypalOrderStatus);
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException($"Order {Id} cannot be reauthorized from status {Status}.");

        Payment.RecordReauthorization(authorizationId, authorizationStatus, expiration);
    }

    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount, string captureRequestId, string? authorizationStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status is OrderStatus.Cancelled or OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount, captureRequestId, authorizationStatus);
        if (Status != OrderStatus.Refunded && Status != OrderStatus.PartiallyRefunded)
            Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus, string voidRequestId)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Order {Id} cannot be cancelled after fulfilment.");

        Payment.RecordVoid(authorizationStatus, voidRequestId);
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string idempotencyKey, decimal amount, string status, string? captureStatus)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new InvalidOperationException($"Order {Id} cannot be refunded from status {Status}.");

        if (amount > Payment.RemainingRefundable)
            throw new InvalidOperationException($"Refund of {amount} exceeds remaining refundable amount {Payment.RemainingRefundable}.");

        var refund = new OrderRefund(paypalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);
        Payment.AddRefundedAmount(amount, captureStatus);
        Status = Payment.RemainingRefundable == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

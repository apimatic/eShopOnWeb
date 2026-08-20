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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment? Payment { get; private set; }

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

    public decimal TotalRefunded()
    {
        return _refunds.Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = Payment?.CapturedAmount ?? 0m;
        return decimal.Round(captured - TotalRefunded(), 2, MidpointRounding.AwayFromZero);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiration,
        decimal authorizedAmount,
        string currency)
    {
        Payment = new OrderPayment(
            paypalOrderId,
            authorizationId,
            authorizationStatus,
            authorizationExpiration,
            authorizedAmount,
            currency);
        Status = OrderStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? expiration)
    {
        Payment?.UpdateAuthorization(authorizationId, status, expiration);
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal paypalFee, decimal netProceeds)
    {
        if (Payment is null)
        {
            throw new InvalidOperationException("Cannot capture an order that has not been authorized.");
        }

        Payment.RecordCapture(captureId, status, capturedAmount, paypalFee, netProceeds);
        Status = OrderStatus.Fulfilled;
    }

    public void RecordVoid(string authorizationStatus)
    {
        Payment?.RecordVoid(authorizationStatus);
        Status = OrderStatus.Cancelled;
    }

    public void CancelWithoutPayment()
    {
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var remaining = RemainingRefundable();
        Status = remaining <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

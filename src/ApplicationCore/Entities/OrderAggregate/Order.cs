using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new();

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

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public bool AlreadyAuthorized() =>
        Status is OrderStatus.Authorized
            or OrderStatus.Fulfilled
            or OrderStatus.PartiallyRefunded
            or OrderStatus.Refunded;

    public bool AlreadyFulfilled() =>
        Status is OrderStatus.Fulfilled
            or OrderStatus.PartiallyRefunded
            or OrderStatus.Refunded;

    public bool AlreadyCancelled() => Status == OrderStatus.Cancelled;

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (AlreadyAuthorized())
        {
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "paid");
        Payment.RecordAuthorization(payPalOrderId, authorizationId, authorizationStatus, authorizedAt, expiresAt, currency);
        Status = OrderStatus.Authorized;
    }

    public void MarkReauthorized(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        EnsureStatus(OrderStatus.Authorized, "reauthorized");
        Payment.RecordReauthorization(authorizationId, authorizationStatus, authorizedAt, expiresAt);
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (AlreadyFulfilled())
        {
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "fulfilled");
        Payment.RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = null)
    {
        if (AlreadyCancelled())
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException(409,
                $"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new PaymentException(409, $"Order {Id} cannot be cancelled because it is {Status}.");
        }

        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            Payment.RecordVoid(authorizationStatus);
        }

        Status = OrderStatus.Cancelled;
    }

    public OrderRefund MarkRefunded(string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException(409,
                $"Order {Id} cannot be refunded because it is {Status}. Refunds are only available after fulfilment.");
        }

        var existing = Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = Payment.RemainingRefundableAmount();
        if (amount > remaining)
        {
            throw new PaymentException(409,
                $"Refund of {amount:0.00} exceeds the remaining captured amount of {remaining:0.00}.");
        }

        var refund = Payment.AddRefund(idempotencyKey, payPalRefundId, status, amount);
        Status = Payment.RemainingRefundableAmount() == 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new PaymentException(409, $"Order {Id} cannot be {action} because it is {Status}.");
        }
    }
}

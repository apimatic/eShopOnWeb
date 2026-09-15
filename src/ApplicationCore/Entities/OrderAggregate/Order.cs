using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public PaymentDetails? Payment { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundableAmount()
    {
        if (Payment == null || string.IsNullOrEmpty(Payment.CaptureId))
        {
            return 0m;
        }

        var remaining = Payment.CapturedAmount - RefundedTotal();
        return remaining < 0 ? 0m : remaining;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void MarkAuthorized(PaymentDetails payment)
    {
        Guard.Against.Null(payment, nameof(payment));

        if (Status == OrderStatus.Authorized &&
            Payment?.AuthorizationId == payment.AuthorizationId)
        {
            Payment = payment;
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "Only an order awaiting payment can be authorized.");
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expirationTime)
    {
        EnsureStatus(OrderStatus.Authorized, "Only an authorized order can have its hold renewed.");
        if (Payment == null)
        {
            throw new PaymentConflictException("The order has no PayPal authorization to renew.");
        }

        Payment.UpdateAuthorization(authorizationId, status, createdAt, expirationTime);
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal paypalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        if (IsCaptured())
        {
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "Only an authorized order can be fulfilled.");
        if (Payment == null || string.IsNullOrEmpty(Payment.AuthorizationId))
        {
            throw new PaymentConflictException("The order has no PayPal authorization to capture.");
        }

        Payment.ApplyCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount, capturedAt);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status == OrderStatus.Fulfilled || Status == OrderStatus.PartiallyRefunded || Status == OrderStatus.Refunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException($"Order in status {Status} cannot be cancelled.");
        }

        if (Payment != null)
        {
            Payment.UpdateAuthorization(Payment.AuthorizationId ?? string.Empty, "VOIDED", Payment.AuthorizationCreatedAt, Payment.AuthorizationExpirationTime);
        }

        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string paypalRefundId, string idempotencyKey, decimal amount, string status)
    {
        if (!IsCaptured())
        {
            throw new PaymentConflictException("Refunds can only be issued after the order has been fulfilled.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = RemainingRefundableAmount();
        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentException($"Refund amount {amount:0.00} exceeds the remaining captured amount {remaining:0.00}.");
        }

        var refund = new PaymentRefund(paypalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        Status = RemainingRefundableAmount() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    public bool IsCaptured() =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded;

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new PaymentConflictException(message);
        }
    }
}

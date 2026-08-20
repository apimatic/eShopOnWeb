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
    public OrderPayment? Payment { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<PaymentRefund> _paymentRefunds = new List<PaymentRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> PaymentRefunds => _paymentRefunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public bool BelongsTo(string buyerId) => BuyerId == buyerId;

    public decimal RemainingRefundableAmount()
    {
        var captured = Payment?.CapturedAmount ?? 0m;
        var refunded = _paymentRefunds.Sum(r => r.Amount);
        var remaining = captured - refunded;
        return remaining < 0 ? 0 : remaining;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _paymentRefunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void RecordAuthorization(OrderPayment payment)
    {
        if (Status == OrderStatus.Authorized && Payment != null)
        {
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "Only an order awaiting payment can be authorized.");
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset authorizedAt,
        DateTimeOffset? authorizationExpiration)
    {
        EnsureStatus(OrderStatus.Authorized, "Only an authorized order can have its hold renewed.");
        if (Payment == null)
        {
            throw new PaymentException(409, "This order has no PayPal authorization to renew.");
        }

        Payment.UpdateAuthorization(authorizationId, authorizationStatus, authorizedAt, authorizationExpiration);
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal payPalFee,
        decimal netAmount)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "Only an authorized order can be fulfilled.");
        if (Payment == null)
        {
            throw new PaymentException(409, "This order has no PayPal authorization to capture.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        Payment?.MarkVoided();
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException(409, "A refund can only be issued after the order has been fulfilled.");
        }

        var remaining = RemainingRefundableAmount();
        if (amount <= 0)
        {
            throw new PaymentException(400, "Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentException(400,
                $"Refund amount {amount:0.00} exceeds the remaining refundable amount {remaining:0.00}.");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, idempotencyKey, amount, status);
        _paymentRefunds.Add(refund);

        Status = RemainingRefundableAmount() == 0 ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new PaymentException(409, $"{message} Current status: {Status}.");
        }
    }
}

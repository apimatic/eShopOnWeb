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

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
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

    public decimal RefundedTotal()
    {
        return _refunds.Where(r => r.CountsAgainstCapturedTotal).Sum(r => r.Amount);
    }

    public decimal RefundableRemaining()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0m ? 0m : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        EnsureCanAuthorize();
        Payment.RecordAuthorization(payPalOrderId, authorizationId, authorizationStatus, expiration, currency);
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException(409, "Only an authorized order can have its payment hold renewed.");
        }

        Payment.RecordReauthorization(authorizationId, authorizationStatus, expiration);
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        if (Status != OrderStatus.Authorized && Status != OrderStatus.Fulfilled)
        {
            throw new PaymentException(409, "Only an authorized order can be fulfilled.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status == OrderStatus.Fulfilled || Status == OrderStatus.Refunded || Status == OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled; issue a refund instead.");
        }

        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new PaymentException(409, $"Order cannot be cancelled while {Status}.");
        }

        Payment.MarkVoided();
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string? status)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "Refunds are only allowed after the order has been fulfilled.");
        }

        var remaining = RefundableRemaining();
        if (amount > remaining)
        {
            throw new PaymentException(409, $"Refund of {amount} exceeds the remaining captured amount of {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        var leftover = RefundableRemaining();
        Status = leftover == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsureCanAuthorize()
    {
        if (Status == OrderStatus.Authorized || Status == OrderStatus.Fulfilled ||
            Status == OrderStatus.Refunded || Status == OrderStatus.PartiallyRefunded)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException(409, $"Order cannot be paid while {Status}.");
        }
    }
}

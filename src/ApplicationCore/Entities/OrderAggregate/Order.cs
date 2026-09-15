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
        PaymentCorrelationId = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string PaymentCorrelationId { get; private set; } = Guid.NewGuid().ToString("N");
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

    public bool OwnedBy(string buyerId) => BuyerId == buyerId;

    public void RecordAuthorization(OrderPayment payment)
    {
        if (Status == OrderStatus.Authorized && Payment is not null)
        {
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "paid");
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        if (IsCaptured())
        {
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "fulfilled");
        if (Payment is null)
        {
            throw new PaymentException("Order has no authorization to capture.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount);
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
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new PaymentException($"Order cannot be cancelled in status {Status}.", 409);
        }

        Payment?.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order cannot be refunded in status {Status}.", 409);
        }

        if (Payment?.CapturedAmount is null)
        {
            throw new PaymentException("Order has no captured payment to refund.", 409);
        }

        var remaining = RemainingRefundableAmount();
        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentException($"Refund of {amount} exceeds remaining refundable amount {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var refunded = RefundedAmount();
        Status = refunded >= Payment.CapturedAmount.Value
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;

        return refund;
    }

    public decimal RemainingRefundableAmount()
    {
        var captured = Payment?.CapturedAmount ?? 0m;
        return captured - RefundedAmount();
    }

    public decimal RefundedAmount()
    {
        return _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
    }

    public bool IsCaptured() =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        && Payment?.CaptureId is not null;

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new PaymentException($"Order cannot be {action} in status {Status}.", 409);
        }
    }
}

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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency = "USD")
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency;
        Status = OrderStatus.AwaitingPayment;
        IdempotencySalt = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; }

    /// <summary>
    /// Random per-order salt used to build PayPal idempotency keys. The database's own auto-increment
    /// Id is not enough on its own: an in-memory-provider run restarts ids from 1 every time the app
    /// restarts, while PayPal's idempotency cache does not reset, so "order-1" from a fresh run could
    /// collide with "order-1" from a previous run. Mixing in this salt keeps every order's PayPal
    /// idempotency keys unique across restarts while staying stable across repeated calls for the
    /// same order (so double-clicking pay/fulfil/cancel is still safely deduplicated).
    /// </summary>
    public Guid IdempotencySalt { get; private set; }

    private OrderPayment? _payment;
    public OrderPayment? Payment => _payment;

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

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

    /// <summary>Records a successful authorization (hold) placed for the order's full total.</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentException($"Order {Id} cannot be authorized because it is in status {Status}, not {OrderStatus.AwaitingPayment}.");
        }

        _payment = new OrderPayment(payPalOrderId, authorizationId, authorizationStatus, Total(), Currency, authorizationExpiresAt);
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Records that a stale authorization was renewed with a fresh honor period.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        if (Status != OrderStatus.PaymentAuthorized || _payment is null)
        {
            throw new OrderPaymentException($"Order {Id} has no active authorization to renew (status {Status}).");
        }

        _payment.UpdateAuthorization(authorizationId, authorizationStatus, authorizationExpiresAt);
    }

    /// <summary>Records that fulfilment captured the held funds; reports what PayPal actually took.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFeeAmount, decimal netAmount)
    {
        if (Status != OrderStatus.PaymentAuthorized || _payment is null)
        {
            throw new OrderPaymentException($"Order {Id} cannot be fulfilled because it has no active payment authorization (status {Status}).");
        }

        _payment.RecordCapture(captureId, captureStatus, capturedAmount, payPalFeeAmount, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Records cancellation before fulfilment; any held funds were released by voiding the authorization.</summary>
    public void RecordCancellation()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new OrderPaymentException($"Order {Id} cannot be cancelled because it is in status {Status}.");
        }

        _payment?.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    /// <summary>
    /// Records a refund against the order's capture. Enforces that the running total of refunds can
    /// never exceed what was actually captured, and that a refund can only happen after fulfilment.
    /// </summary>
    public OrderRefund RecordRefund(string refundId, string idempotencyKey, decimal amount, string status, DateTimeOffset createdAt)
    {
        if ((Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded) || _payment?.CapturedAmount is null)
        {
            throw new OrderPaymentException($"Order {Id} cannot be refunded because it has no captured payment (status {Status}).");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var remaining = _payment.CapturedAmount.Value - _payment.RefundedAmount;
        if (amount > remaining)
        {
            throw new OrderPaymentException($"Order {Id} cannot be refunded {amount:0.00} {Currency} because only {remaining:0.00} {Currency} remains refundable.");
        }

        var refund = new OrderRefund(refundId, idempotencyKey, amount, status, createdAt);
        _refunds.Add(refund);
        _payment.RecordRefund(amount, status);

        Status = _payment.RefundedAmount >= _payment.CapturedAmount.Value
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;

        return refund;
    }
}

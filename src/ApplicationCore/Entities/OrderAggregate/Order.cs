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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        Payment = new OrderPayment();
        PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public OrderPayment Payment { get; private set; } = new OrderPayment();
    public string PaymentIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");

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

    public decimal RefundedTotal()
    {
        return _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt,
        string? cardLastDigits,
        string? cardBrand)
    {
        EnsureStatus(OrderPaymentStatus.AwaitingPayment, "authorized");
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        Payment.RecordAuthorization(
            payPalOrderId,
            authorizationId,
            status,
            amount,
            currency,
            createdAt,
            expiresAt,
            cardLastDigits,
            cardBrand);
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void MarkReauthorized(string newAuthorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        EnsureStatus(OrderPaymentStatus.Authorized, "reauthorized");
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        Payment.RecordReauthorization(newAuthorizationId, status, createdAt, expiresAt);
    }

    public void MarkFulfilled(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount,
        DateTimeOffset capturedAt)
    {
        EnsureStatus(OrderPaymentStatus.Authorized, "fulfilled");
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        Payment.RecordCapture(captureId, status, capturedAmount, paypalFee, netAmount, capturedAt);
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled(DateTimeOffset cancelledAt)
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.AwaitingPayment)
        {
            Payment.RecordVoid(cancelledAt);
            PaymentStatus = OrderPaymentStatus.Cancelled;
            return;
        }

        EnsureStatus(OrderPaymentStatus.Authorized, "cancelled");
        Payment.RecordVoid(cancelledAt);
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount, string currency)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new CheckoutException(409, $"Order {Id} cannot be refunded while payment status is {PaymentStatus}.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (amount > RemainingRefundable())
        {
            throw new CheckoutException(409,
                $"Refund of {amount} exceeds remaining refundable amount {RemainingRefundable()} for order {Id}.");
        }

        var refund = new OrderRefund(idempotencyKey, payPalRefundId, status, amount, currency);
        _refunds.Add(refund);

        PaymentStatus = RemainingRefundable() <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;

        return refund;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    private void EnsureStatus(OrderPaymentStatus expected, string action)
    {
        if (PaymentStatus != expected)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be {action} while payment status is {PaymentStatus}.");
        }
    }
}

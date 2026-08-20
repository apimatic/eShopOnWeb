using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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
        return PayPalMoney.Round(total);
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public OrderPayment EnsurePayment(string currency)
    {
        Payment ??= new OrderPayment(Id, currency);
        return Payment;
    }

    public void RecordAuthorization(
        string currency,
        string paypalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset authorizedAt,
        string? last4,
        string? brand,
        int? savedPaymentMethodId)
    {
        EnsureNotCancelled();
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(409, $"Order {Id} has already been fulfilled and cannot be authorized again.");
        }

        var payment = EnsurePayment(currency);
        payment.RecordAuthorization(paypalOrderId, authorizationId, status, expiration, authorizedAt, last4, brand, savedPaymentMethodId);
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiration, DateTimeOffset authorizedAt)
    {
        if (Payment is null)
        {
            throw new OrderPaymentException(409, $"Order {Id} has no authorization to renew.");
        }

        Payment.RecordReauthorization(authorizationId, status, expiration, authorizedAt);
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return;
        }

        if (Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentException(409, $"Order {Id} must be authorized before it can be fulfilled.");
        }

        if (Payment is null)
        {
            throw new OrderPaymentException(409, $"Order {Id} has no payment to capture.");
        }

        Payment.RecordCapture(captureId, status, capturedAmount, paypalFee, netAmount, capturedAt);
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus)
    {
        if (Status is OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(409, $"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        Payment?.RecordVoid(authorizationStatus);
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.AwaitingPayment or OrderStatus.Authorized)
        {
            throw new OrderPaymentException(409, $"Order {Id} cannot be refunded until it has been fulfilled.");
        }

        if (Payment is null || string.IsNullOrWhiteSpace(Payment.CaptureId))
        {
            throw new OrderPaymentException(409, $"Order {Id} has no captured payment to refund.");
        }

        var existing = Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = Payment.RefundableRemaining;
        if (amount <= 0)
        {
            throw new OrderPaymentException(400, "Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new OrderPaymentException(409,
                $"Refund of {PayPalMoney.ToValue(amount)} exceeds the remaining refundable amount {PayPalMoney.ToValue(remaining)}.");
        }

        var refund = Payment.AddRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        RefreshRefundStatus();
        return refund;
    }

    public void RefreshRefundStatus()
    {
        if (Payment is null || Status is OrderStatus.Cancelled or OrderStatus.AwaitingPayment or OrderStatus.Authorized)
        {
            return;
        }

        var remaining = Payment.RefundableRemaining;
        if (remaining <= 0 && Payment.CapturedAmount > 0)
        {
            Status = OrderStatus.Refunded;
        }
        else if (Payment.RefundedAmount > 0)
        {
            Status = OrderStatus.PartiallyRefunded;
        }
        else
        {
            Status = OrderStatus.Fulfilled;
        }
    }

    private void EnsureNotCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new OrderPaymentException(409, $"Order {Id} has been cancelled.");
        }
    }
}

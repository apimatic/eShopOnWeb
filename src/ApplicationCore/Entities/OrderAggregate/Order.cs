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
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

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

    public void EnsureOwnedBy(string buyerId)
    {
        if (!string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenAccessException("The requested order does not belong to the caller.");
        }
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be authorized from status {Status}.");
        }

        Payment.RecordAuthorization(
            payPalOrderId,
            payPalOrderStatus,
            authorizationId,
            authorizationStatus,
            createdAt,
            expiresAt,
            currency);

        Status = OrderStatus.Authorized;
    }

    public void MarkReauthorized(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be reauthorized from status {Status}.");
        }

        Payment.RecordReauthorization(authorizationId, authorizationStatus, createdAt, expiresAt);
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset? capturedAt,
        string? authorizationStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status is not OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Payment.RecordCapture(
            captureId,
            captureStatus,
            capturedAmount,
            payPalFee,
            netAmount,
            capturedAt,
            authorizationStatus);

        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus, string? payPalOrderStatus)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException(
                $"Order {Id} has already been fulfilled. Cancel is only available before fulfilment; use a refund instead.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            Payment.RecordVoid(authorizationStatus, payPalOrderStatus);
        }

        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(
        string payPalRefundId,
        string payPalRefundStatus,
        string idempotencyKey,
        decimal amount,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException(
                $"Order {Id} cannot be refunded from status {Status}. Refunds are only available after fulfilment.");
        }

        var remaining = Payment.RemainingRefundableAmount;
        if (amount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {amount} exceeds the remaining refundable amount of {remaining} {Payment.Currency}.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refund = new PaymentRefund(payPalRefundId, payPalRefundStatus, idempotencyKey, amount, currency);
        _refunds.Add(refund);
        Payment.AddRefundedAmount(amount);

        Status = Payment.RemainingRefundableAmount == 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;

        return refund;
    }
}

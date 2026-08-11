using System;
using System.Collections.Generic;
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

    /// <summary>Payment/fulfilment lifecycle state. Additive to the original model.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>The PayPal-owned payment state. Null until the order is paid (authorized).</summary>
    public Payment? Payment { get; private set; }

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

    // --- Payment lifecycle behaviour (Order is the aggregate root that guards it) ---

    /// <summary>
    /// Attach the hold created at PayPal and move the order to <see cref="OrderStatus.Authorized"/>.
    /// </summary>
    public void MarkAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be authorized from status {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Renew a stale authorization hold with a fresh one from PayPal.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        RequirePayment();
        Payment!.RenewAuthorization(authorizationId, status, expiresAt);
    }

    /// <summary>
    /// Capture the hold at fulfilment (money is taken now) and move to <see cref="OrderStatus.Fulfilled"/>.
    /// </summary>
    public void MarkFulfilled(string captureId, string status, decimal gross, decimal? fee, decimal? net)
    {
        RequirePayment();
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Payment!.RecordCapture(captureId, status, gross, fee, net);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Release the hold before fulfilment; no money ever moved.</summary>
    public void MarkCancelled()
    {
        RequirePayment();
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be cancelled from status {Status}.");
        }

        Payment!.RecordVoided();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Record a (partial or full) refund against the capture and update the order's refund status.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        RequirePayment();
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded from status {Status}.");
        }

        // Defence in depth: the aggregate itself never lets refunds exceed the captured amount.
        if (amount > Payment!.RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the {Payment.RefundableRemaining} still refundable on order {Id}.");
        }

        var refund = Payment.AddRefund(payPalRefundId, amount, status, idempotencyKey);
        Status = Payment.RefundableRemaining <= 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
        return refund;
    }

    private void RequirePayment()
    {
        if (Payment is null)
        {
            throw new InvalidOperationException($"Order {Id} has no payment.");
        }
    }
}

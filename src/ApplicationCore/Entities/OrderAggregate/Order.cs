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

    // Payment state owned by PayPal; persisted here so any later request can act on it.
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

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

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining() =>
        CapturedAmount.HasValue ? CapturedAmount.Value - TotalRefunded() : 0m;

    /// <summary>
    /// Records a successful authorization. Returns false when the order already carries
    /// this authorization (idempotent replay) so the caller can skip a second hold.
    /// </summary>
    public bool MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string currency)
    {
        if (Status == OrderStatus.PaymentAuthorized && AuthorizationId == authorizationId)
        {
            return false;
        }

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {Id} is {Status} and cannot be authorized again.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Currency = currency;
        Status = OrderStatus.PaymentAuthorized;
        return true;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentStateException($"Order {Id} is {Status}; its authorization cannot be renewed.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>
    /// Records a successful capture. Returns false when this capture was already recorded
    /// (idempotent replay).
    /// </summary>
    public bool MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        if (Status == OrderStatus.Fulfilled && CaptureId == captureId)
        {
            return false;
        }

        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentStateException($"Order {Id} is {Status} and cannot be fulfilled.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = OrderStatus.Fulfilled;
        return true;
    }

    /// <summary>
    /// Records release of the held funds. Returns false when already cancelled.
    /// </summary>
    public bool MarkCancelled(string? authorizationStatus)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status == OrderStatus.Fulfilled)
        {
            throw new PaymentStateException($"Order {Id} is fulfilled; refund it instead of cancelling.");
        }

        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = OrderStatus.Cancelled;
        return true;
    }

    /// <summary>
    /// Returns an existing refund made under the same idempotency key, if any.
    /// </summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public OrderRefund AddRefund(string refundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        if (Status != OrderStatus.Fulfilled)
        {
            throw new PaymentStateException($"Order {Id} is {Status}; only fulfilled orders can be refunded.");
        }

        if (amount > RefundableRemaining())
        {
            throw new PaymentStateException(
                $"Refund of {amount} {currency} exceeds the refundable remainder " +
                $"({RefundableRemaining()} {currency} of {CapturedAmount} {currency} captured).");
        }

        var refund = new OrderRefund(refundId, amount, currency, status, idempotencyKey);
        _refunds.Add(refund);
        CaptureStatus = TotalRefunded() >= CapturedAmount ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

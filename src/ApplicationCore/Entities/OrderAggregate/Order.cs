using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
        : this(buyerId, shipToAddress, items, "USD")
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency.ToUpperInvariant();
    }

    public string BuyerId { get; private set; }
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Pending;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public byte[] Version { get; private set; } = [];
    public long PaymentOperationSequence { get; private set; }

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

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds
        .Where(x => x.Status is PaymentOperationStatus.Pending or PaymentOperationStatus.Unknown or PaymentOperationStatus.Completed)
        .Sum(x => x.Amount);

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void BeginAuthorization()
    {
        if (PaymentStatus is not (PaymentStatus.AwaitingPayment or PaymentStatus.AuthorizationPending or PaymentStatus.AuthorizationFailed))
            throw new InvalidOperationException("This order is not awaiting payment.");
        PaymentStatus = PaymentStatus.AuthorizationPending;
    }

    public void RecordPayPalOrder(string payPalOrderId, string? status)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
    }

    public void MarkAuthorized(string authorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed() => PaymentStatus = PaymentStatus.AuthorizationFailed;

    public void BeginCapture()
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.CapturePending or PaymentStatus.CaptureFailed))
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        PaymentStatus = PaymentStatus.CapturePending;
    }

    public void MarkCaptured(string captureId, string status, decimal amount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCapturePending(string captureId, string status, decimal amount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PaymentStatus = PaymentStatus.CapturePending;
    }

    public void MarkCaptureFailed() => PaymentStatus = PaymentStatus.CaptureFailed;

    public void BeginCancellation()
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.CancellationPending))
            throw new InvalidOperationException("Only an authorized, uncaptured order can be cancelled.");
        PaymentStatus = PaymentStatus.CancellationPending;
    }

    public void MarkCancelled(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Cancelled;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public OrderRefund ReserveRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (existing.Amount != amount)
                throw new InvalidOperationException("The idempotency key was already used with a different amount.");
            return existing;
        }
        if (CapturedAmount is null || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the captured amount remaining.");
        var refund = new OrderRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        PaymentOperationSequence++;
        return refund;
    }

    public void RefreshRefundState()
    {
        if (CapturedAmount is null) return;
        var completed = _refunds.Where(x => x.Status == PaymentOperationStatus.Completed).Sum(x => x.Amount);
        PaymentStatus = completed >= CapturedAmount.Value ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

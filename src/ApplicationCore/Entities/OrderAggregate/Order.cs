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
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        PaymentOperationId = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public string PaymentOperationId { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

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

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount() => _refunds
        .Where(x => x.Status == "COMPLETED" || x.Status == "PENDING")
        .Sum(x => x.Amount);

    public void RecordPayPalOrder(string paypalOrderId, string providerStatus, string currency)
    {
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = providerStatus;
        Currency = currency;
    }

    public void RecordAuthorization(string authorizationId, string providerStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = providerStatus;
        AuthorizedAmount = amount;
        OriginalAuthorizationCreatedAt ??= createdAt;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string providerStatus, decimal amount,
        decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = providerStatus;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        if (providerStatus == "COMPLETED")
        {
            PaymentStatus = OrderPaymentStatus.Captured;
            FulfilledAt ??= DateTimeOffset.UtcNow;
        }
    }

    public void Cancel(string providerStatus)
    {
        AuthorizationStatus = providerStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    public void RefreshRefundState()
    {
        var refunded = _refunds.Where(x => x.Status == "COMPLETED").Sum(x => x.Amount);
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (refunded > 0)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }
}

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

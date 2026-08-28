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
        : this(buyerId, shipToAddress, items, string.Empty)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency;
        PaymentReference = $"ESHOP-{Guid.NewGuid():N}";
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        FulfillmentStatus = OrderFulfillmentStatus.Pending;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public OrderFulfillmentStatus FulfillmentStatus { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }

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

    public void RecordPayPalOrder(string paypalOrderId)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizedAmount = amount;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? paypalFee,
        decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        PaymentStatus = OrderPaymentStatus.Captured;
        FulfillmentStatus = OrderFulfillmentStatus.Fulfilled;
    }

    public void Cancel(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
        FulfillmentStatus = OrderFulfillmentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string idempotencyKey, string paypalRefundId, decimal amount,
        string status, DateTimeOffset createdAt)
    {
        var refund = new OrderRefund(idempotencyKey, paypalRefundId, amount, status, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = CapturedAmount.HasValue && RefundedAmount >= CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
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

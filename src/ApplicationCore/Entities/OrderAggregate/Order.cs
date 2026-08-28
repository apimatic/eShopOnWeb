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
    }

    public string BuyerId { get; private set; }
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void BeginPayment(string currency, string paypalOrderId, string paypalOrderStatus)
    {
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.PaymentRequired))
        {
            throw new InvalidOperationException($"Order {Id} cannot begin payment while it is {Status}.");
        }

        PaymentCurrency = currency;
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
    }

    public void RecordAuthorization(string currency, string paypalOrderId, string paypalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.PaymentRequired))
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid while it is {Status}.");
        }

        if (PayPalOrderId is not null && !string.Equals(PayPalOrderId, paypalOrderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The PayPal order does not match the active payment attempt.");
        }

        PaymentCurrency = currency;
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        OriginalAuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be reauthorized while it is {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RequireNewPayment(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = OrderStatus.PaymentRequired;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        if (Status is not (OrderStatus.Authorized or OrderStatus.FulfilmentPending))
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled while it is {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? OrderStatus.Fulfilled
            : string.Equals(captureStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                ? OrderStatus.FulfilmentPending
                : OrderStatus.PaymentRequired;
    }

    public void Cancel(string authorizationStatus)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded or OrderStatus.FulfilmentPending)
        {
            throw new InvalidOperationException($"Order {Id} has been captured and cannot be cancelled.");
        }

        AuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund RecordRefund(string paypalRefundId, string idempotencyKey, string status,
        decimal amount, DateTimeOffset createdAt)
    {
        if (CapturedAmount is null || Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException($"Order {Id} does not have a refundable capture.");
        }

        if (amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
        {
            throw new InvalidOperationException("Refund amount exceeds the remaining captured amount.");
        }

        var refund = new PaymentRefund(paypalRefundId, idempotencyKey, status, amount, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount == CapturedAmount.Value ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

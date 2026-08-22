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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;
    public string? CurrencyCode { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

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

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void MarkAwaitingPayment(string currencyCode)
    {
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        CurrencyCode = currencyCode;
        Status = OrderStatus.AwaitingPayment;
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? expirationTime,
        DateTimeOffset? createTime)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationExpiresAt = expirationTime;
        AuthorizationCreatedAt = createTime;
        OriginalAuthorizationCreatedAt ??= createTime ?? DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expirationTime, DateTimeOffset? createTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationExpiresAt = expirationTime;
        AuthorizationCreatedAt = createTime;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = null)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new CheckoutException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (authorizationStatus != null)
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }

        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new CheckoutException("Only a fulfilled order can be refunded.", 409);
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new CheckoutException(
                $"Refund of {amount} exceeds the remaining captured amount of {remaining}.", 409);
        }

        var refund = new OrderRefund(idempotencyKey, paypalRefundId, status, amount, currencyCode);
        _refunds.Add(refund);

        Status = RemainingRefundable() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

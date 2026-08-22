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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }

    public decimal RefundedAmount { get; private set; }

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

    public decimal RemainingRefundable => Math.Max(0, (CapturedAmount ?? 0m) - RefundedAmount);

    public string InvoiceId => $"ESHOP-{Id}";

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public bool OwnedBy(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    public void AttachPayPalOrder(string paypalOrderId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
    }

    public void MarkAuthorized(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot authorize an order in {Status} state.");
        }

        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
        OriginalAuthorizedAt ??= AuthorizedAt;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));
        Guard.Against.Negative(paypalFee, nameof(paypalFee));

        if (Status is OrderStatus.Cancelled or OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Cannot fulfil an order in {Status} state.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Cannot cancel an order after funds have been captured. Issue a refund instead.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public bool HasRefundWithIdempotencyKey(string idempotencyKey) =>
        _refunds.Any(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public OrderRefund RecordRefund(string paypalRefundId, string paypalRefundStatus, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Refunds are only allowed after the order has been fulfilled.");
        }

        if (HasRefundWithIdempotencyKey(idempotencyKey))
        {
            return _refunds.First(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        }

        if (amount > RemainingRefundable)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining captured amount of {RemainingRefundable}.");
        }

        var refund = new OrderRefund(paypalRefundId, paypalRefundStatus, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RemainingRefundable == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        if (PayPalCaptureStatus is not null)
        {
            PayPalCaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

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

    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiration { get; private set; }
    public DateTimeOffset? PayPalAuthorizationCreatedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public string? PayIdempotencyKey { get; private set; }
    public string? CaptureIdempotencyKey { get; private set; }
    public string? CancelIdempotencyKey { get; private set; }

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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public string EnsurePayIdempotencyKey()
    {
        if (string.IsNullOrEmpty(PayIdempotencyKey))
        {
            PayIdempotencyKey = Guid.NewGuid().ToString("N");
        }

        return PayIdempotencyKey;
    }

    public string EnsureCaptureIdempotencyKey()
    {
        if (string.IsNullOrEmpty(CaptureIdempotencyKey))
        {
            CaptureIdempotencyKey = Guid.NewGuid().ToString("N");
        }

        return CaptureIdempotencyKey;
    }

    public string EnsureCancelIdempotencyKey()
    {
        if (string.IsNullOrEmpty(CancelIdempotencyKey))
        {
            CancelIdempotencyKey = Guid.NewGuid().ToString("N");
        }

        return CancelIdempotencyKey;
    }

    public void RecordPayPalOrder(string paypalOrderId, string currency)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        PayPalOrderId = paypalOrderId;
        Currency = currency;
    }

    public void MarkAuthorized(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        DateTimeOffset? createdAt,
        string currency,
        string? invoiceId = null)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentOperationException(409, $"Order {Id} cannot be authorized from status {Status}.");
        }

        PayPalOrderId = paypalOrderId;
        PayPalInvoiceId = string.IsNullOrWhiteSpace(invoiceId) ? PayPalInvoiceId : invoiceId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
        PayPalAuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        Currency = currency;
        Status = OrderStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        if (Status != OrderStatus.Authorized && Status != OrderStatus.Fulfilled)
        {
            throw new PaymentOperationException(409, $"Order {Id} cannot be fulfilled from status {Status}.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentOperationException(409,
                $"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        PayPalAuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string paypalRefundStatus, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentOperationException(409, $"Order {Id} cannot be refunded from status {Status}.");
        }

        var remaining = RemainingRefundable();
        if (amount - remaining > 0.0001m)
        {
            throw new PaymentOperationException(409,
                $"Refund of {amount} exceeds the remaining refundable amount {remaining} for order {Id}.");
        }

        var refund = new OrderRefund(paypalRefundId, paypalRefundStatus, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        Status = leftover <= 0.0001m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        if (!string.IsNullOrEmpty(PayPalCaptureStatus))
        {
            PayPalCaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

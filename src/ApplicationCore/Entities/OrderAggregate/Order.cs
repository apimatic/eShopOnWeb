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

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public decimal RefundedTotal()
    {
        return decimal.Round(_refunds.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);
    }

    public decimal RemainingRefundableAmount()
    {
        var captured = CapturedAmount ?? 0m;
        return decimal.Round(captured - RefundedTotal(), 2, MidpointRounding.AwayFromZero);
    }

    public void AttachPayPalOrder(string payPalOrderId, string? payPalOrderStatus, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        PayPalInvoiceId = invoiceId;
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} in status {Status} cannot be authorized.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        OriginalAuthorizedAt ??= DateTimeOffset.UtcNow;
        PaymentCurrency = currency;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} must be authorized before the hold can be renewed.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount,
        string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));
        Guard.Against.Negative(paypalFee, nameof(paypalFee));

        if (Status != OrderStatus.Authorized && Status != OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException($"Order {Id} in status {Status} cannot be fulfilled.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = decimal.Round(paypalFee, 2, MidpointRounding.AwayFromZero);
        NetAmount = decimal.Round(netAmount, 2, MidpointRounding.AwayFromZero);
        PaymentCurrency = currency;
        AuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public OrderRefund AddRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new InvalidOrderStateException($"Order {Id} must be fulfilled before it can be refunded.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded > RemainingRefundableAmount())
        {
            throw new InvalidOrderStateException(
                $"Refund of {rounded} exceeds the remaining refundable amount {RemainingRefundableAmount()} for order {Id}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, rounded, currency, idempotencyKey);
        _refunds.Add(refund);

        var remaining = RemainingRefundableAmount();
        Status = remaining <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        if (CaptureStatus != null)
        {
            CaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

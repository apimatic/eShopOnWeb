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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalInvoiceId { get; private set; }

    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
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

    public decimal RefundedTotal()
    {
        return _refunds
            .Where(r => !string.Equals(r.PayPalRefundStatus, "FAILED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.PayPalRefundStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public void AttachPayPalOrder(string paypalOrderId, string paypalOrderStatus, string currency)
    {
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        Currency = currency;
    }

    public void EnsurePayPalInvoiceId()
    {
        if (string.IsNullOrWhiteSpace(PayPalInvoiceId))
        {
            PayPalInvoiceId = $"ew{Id}-{Guid.NewGuid():N}";
        }
    }

    public void MarkAuthorized(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {Id} cannot be authorized from status {PaymentStatus}.");
        }

        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string? authorizationStatus = null)
    {
        if (PaymentStatus == OrderPaymentStatus.Fulfilled
            || PaymentStatus == OrderPaymentStatus.Refunded
            || PaymentStatus == OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {Id} cannot be fulfilled from status {PaymentStatus}.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        if (!string.IsNullOrEmpty(authorizationStatus))
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string paypalRefundStatus, string idempotencyKey, decimal amount)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {Id} cannot be refunded from status {PaymentStatus}.");
        }

        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (amount > RemainingRefundable())
        {
            throw new PaymentException($"Refund of {amount} exceeds the remaining refundable amount of {RemainingRefundable()}.");
        }

        var refund = new OrderRefund(paypalRefundId, paypalRefundStatus, idempotencyKey, amount);
        _refunds.Add(refund);
        RefreshRefundStatus();
        return refund;
    }

    public void RefreshRefundStatus()
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded))
        {
            return;
        }

        var remaining = RemainingRefundable();
        PaymentStatus = remaining <= 0m
            ? OrderPaymentStatus.Refunded
            : RefundedTotal() > 0m
                ? OrderPaymentStatus.PartiallyRefunded
                : OrderPaymentStatus.Fulfilled;
    }
}

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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? currency = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency;
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
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : decimal.Round(remaining, 2);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AssignCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency ??= currency;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status == OrderStatus.Authorized &&
            string.Equals(PayPalAuthorizationId, authorizationId, StringComparison.Ordinal))
        {
            PayPalAuthorizationStatus = status;
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "pay");

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        EnsureStatus(OrderStatus.Authorized, "reauthorize");

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        if (createdAt.HasValue)
        {
            AuthorizationCreatedAt = createdAt;
        }
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (IsCaptured && string.Equals(PayPalCaptureId, captureId, StringComparison.Ordinal))
        {
            PayPalCaptureStatus = status;
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "fulfil");

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = decimal.Round(capturedAmount, 2);
        PaypalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2) : paypalFee;
        NetAmount = netAmount.HasValue ? decimal.Round(netAmount.Value, 2) : netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation(string? authorizationStatus = "VOIDED")
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be cancelled while it is {Status}.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (!IsCaptured)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be refunded until it has been fulfilled.");
        }

        var refundAmount = decimal.Round(amount, 2);
        if (refundAmount <= 0)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        }

        var remaining = RefundableAmount();
        if (refundAmount > remaining)
        {
            throw new CheckoutException(409,
                $"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, refundAmount, Currency ?? "USD", status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableAmount() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    public bool IsCaptured =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded;

    public IEnumerable<string> PayPalIdentifiers()
    {
        if (!string.IsNullOrEmpty(PayPalOrderId)) yield return PayPalOrderId;
        if (!string.IsNullOrEmpty(PayPalAuthorizationId)) yield return PayPalAuthorizationId;
        if (!string.IsNullOrEmpty(PayPalCaptureId)) yield return PayPalCaptureId;
        foreach (var refund in _refunds)
        {
            if (!string.IsNullOrEmpty(refund.PayPalRefundId))
            {
                yield return refund.PayPalRefundId;
            }
        }
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new CheckoutException(409, $"Order {Id} cannot {action} while it is {Status}.");
        }
    }
}

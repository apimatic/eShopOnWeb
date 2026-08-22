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

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    public decimal RoundedTotal() => decimal.Round(Total(), 2, MidpointRounding.AwayFromZero);

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount.GetValueOrDefault();
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void AttachPayPalCheckoutId(string paypalOrderId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
    }

    public void MarkAuthorized(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        }

        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Currency = currency;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot refresh an authorization from status {Status}.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
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
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2, MidpointRounding.AwayFromZero) : null;
        NetAmount = netAmount.HasValue ? decimal.Round(netAmount.Value, 2, MidpointRounding.AwayFromZero) : null;
        PayPalAuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}.");
        }

        if (Status == OrderStatus.Authorized)
        {
            PayPalAuthorizationStatus = "VOIDED";
        }

        CancelledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded from status {Status}.");
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        }

        var remaining = RefundableRemaining();
        if (rounded > remaining)
        {
            throw new InvalidOperationException(
                $"Refund of {rounded} exceeds the remaining refundable amount of {remaining}.");
        }

        var refund = new OrderRefund(paypalRefundId, status, rounded, currency, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private Order() { }
#pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderStatus.PendingPayment;
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;
    public OrderPayment Payment { get; private set; } = new();

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public bool IsOwnedBy(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void EnsureOwnedBy(string buyerId)
    {
        if (!IsOwnedBy(buyerId))
        {
            throw new PaymentException("This order does not belong to the signed-in shopper.", HttpStatusCode.Forbidden);
        }
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt,
        string currency,
        string? cardBrand,
        string? cardLast4)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException($"An order in status {Status} cannot be authorized.", HttpStatusCode.Conflict);
        }

        Payment.SetCurrency(currency);
        Payment.RecordAuthorization(payPalOrderId, authorizationId, status, createdAt, expiresAt, cardBrand, cardLast4);
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string status, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException("Only an authorized order can have its hold renewed.", HttpStatusCode.Conflict);
        }

        Payment.ReplaceAuthorization(authorizationId, status, createdAt, expiresAt);
    }

    public void MarkFulfilled(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        if (Status == OrderStatus.Fulfilled || Status == OrderStatus.PartiallyRefunded || Status == OrderStatus.Refunded)
        {
            return;
        }

        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException("Only an authorized order can be fulfilled.", HttpStatusCode.Conflict);
        }

        Payment.RecordCapture(captureId, status, capturedAmount, paypalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; refund it instead.", HttpStatusCode.Conflict);
        }

        Payment.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", HttpStatusCode.Conflict);
        }

        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        var remaining = RemainingRefundable();
        if (amount - remaining > 0.001m)
        {
            throw new PaymentException($"Refund of {amount:0.00} exceeds the remaining captured amount of {remaining:0.00}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey, DateTimeOffset.UtcNow);
        _refunds.Add(refund);

        Status = RemainingRefundable() <= 0.001m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

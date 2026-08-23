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
        Status = OrderStatus.PendingPayment;
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
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

    public decimal RefundableRemaining()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void MarkAuthorized(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency)
    {
        if (Status != OrderStatus.PendingPayment)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be authorized from status {Status}.");
        }

        if (authorizedAmount != Total())
        {
            throw new PaymentException(
                $"PayPal authorized {authorizedAmount} but the order total is {Total()}.", 502);
        }

        Payment.RecordAuthorization(
            paypalOrderId, authorizationId, authorizationStatus, expiration, authorizedAmount, currency);
        Status = OrderStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentStateException($"Order {Id} is not authorized.");
        }

        Payment.RecordReauthorization(authorizationId, authorizationStatus, expiration);
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentStateException(
                $"Order {Id} has already been captured and cannot be cancelled. Issue a refund instead.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.PendingPayment && Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be cancelled from status {Status}.");
        }

        if (!string.IsNullOrEmpty(authorizationStatus))
        {
            Payment.RecordVoid(authorizationStatus);
        }

        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be refunded from status {Status}.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = RefundableRemaining();
        if (amount > remaining)
        {
            throw new PaymentException(
                $"Refund of {amount} exceeds remaining captured funds {remaining}.", 400);
        }

        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() == 0 ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

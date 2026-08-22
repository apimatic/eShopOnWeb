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
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.None;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

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

    public void AwaitPayment(string? currency = null)
    {
        if (PaymentStatus != OrderPaymentStatus.None && PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {Id} cannot await payment from status {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        if (!string.IsNullOrWhiteSpace(currency))
        {
            Payment.SetConfiguredCurrency(currency);
        }
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiration,
        string currency)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {Id} is cancelled and cannot be authorized.");
        }

        Payment.RecordAuthorization(
            paypalOrderId,
            paypalOrderStatus,
            authorizationId,
            authorizationStatus,
            authorizationCreatedAt,
            authorizationExpiration,
            currency);
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RefreshAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {Id} must be authorized before the hold can be renewed.");
        }

        Payment.UpdateAuthorization(authorizationId, authorizationStatus, authorizationCreatedAt, authorizationExpiration);
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds,
        string? currency = null)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));
        Guard.Against.Negative(paypalFee, nameof(paypalFee));

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {Id} must be authorized before it can be fulfilled.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netProceeds, currency);
        Payment.UpdateAuthorizationStatus("CAPTURED");
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void VoidAuthorization(string? paypalOrderStatus = null)
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {Id} has already been fulfilled. Use a refund instead of cancelling.");
        }

        if (PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.Authorized or OrderPaymentStatus.None))
        {
            throw new PaymentConflictException($"Order {Id} cannot be cancelled from status {PaymentStatus}.");
        }

        Payment.UpdateAuthorizationStatus("VOIDED");
        if (!string.IsNullOrWhiteSpace(paypalOrderStatus))
        {
            Payment.UpdatePayPalOrderStatus(paypalOrderStatus);
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public decimal RefundedTotal()
    {
        return _refunds
            .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Order {Id} must be fulfilled before it can be refunded.");
        }

        var remaining = RemainingRefundable();
        if (amount - remaining > 0.0000001m)
        {
            throw new PaymentConflictException(
                $"Refund of {amount} exceeds the remaining captured amount of {remaining} for order {Id}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        PaymentStatus = leftover <= 0.0000001m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        Payment.UpdateCaptureStatus(PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED");

        return refund;
    }

    public bool AuthorizationHonorPeriodElapsed(DateTimeOffset utcNow)
    {
        if (Payment.AuthorizationCreatedAt is DateTimeOffset created)
        {
            return utcNow >= created.AddDays(3);
        }

        return false;
    }

    public bool AuthorizationHasExpired(DateTimeOffset utcNow)
    {
        if (Payment.AuthorizationExpiration is DateTimeOffset expiration)
        {
            return utcNow >= expiration;
        }

        return false;
    }
}

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
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public string PayPalInvoiceId => $"ESHOP-{Id}-{OrderDate.UtcTicks}";
    public string PayPalCustomId => $"eshop-order-{Id}-{OrderDate.UtcTicks}";

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
        return _refunds.Sum(r => r.Amount);
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

    public void AttachPayPalOrder(string payPalOrderId, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        PayPalOrderId = payPalOrderId;
        Currency = currency;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiresAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {Id} cannot be authorized while in status {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = authorizationCreatedAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Currency = currency;
        PaidAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {Id} cannot refresh an authorization while in status {Status}.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = authorizationCreatedAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal payPalFee,
        decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));
        Guard.Against.Negative(payPalFee, nameof(payPalFee));

        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {Id} cannot be fulfilled while in status {Status}.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation(string? authorizationStatus = "VOIDED")
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException($"Order {Id} cannot be cancelled after fulfilment.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        CancelledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string refundStatus, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(refundStatus, nameof(refundStatus));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Order {Id} cannot be refunded while in status {Status}.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (amount > RemainingRefundable())
        {
            throw new PaymentConflictException(
                $"Refund of {amount} exceeds remaining refundable amount {RemainingRefundable()} for order {Id}.");
        }

        var refund = new OrderRefund(payPalRefundId, refundStatus, amount, idempotencyKey);
        _refunds.Add(refund);

        if (RemainingRefundable() == 0m)
        {
            Status = OrderStatus.Refunded;
            PayPalCaptureStatus = "REFUNDED";
        }
        else
        {
            Status = OrderStatus.PartiallyRefunded;
            PayPalCaptureStatus = "PARTIALLY_REFUNDED";
        }

        return refund;
    }

    public bool RequiresAuthorizationRenewal(DateTimeOffset utcNow)
    {
        if (Status != OrderStatus.Authorized)
        {
            return false;
        }

        if (AuthorizationExpiresAt is not null && utcNow >= AuthorizationExpiresAt.Value)
        {
            return true;
        }

        if (AuthorizationCreatedAt is not null && utcNow >= AuthorizationCreatedAt.Value.AddDays(3))
        {
            return true;
        }

        return false;
    }

    public bool CanRenewAuthorization(DateTimeOffset utcNow)
    {
        if (AuthorizationCreatedAt is null)
        {
            return false;
        }

        var originalWindowEnd = AuthorizationCreatedAt.Value.AddDays(29);
        return utcNow < originalWindowEnd;
    }
}

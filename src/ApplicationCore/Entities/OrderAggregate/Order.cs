using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() { }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        Guard.Against.Null(items, nameof(items));
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentReference = $"ESHOP-{Guid.NewGuid():N}";
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.None;
    public string PaymentReference { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public int PaymentAttempt { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public void RecordPaymentFailure()
    {
        PaymentAttempt++;
        PaymentStatus = PaymentStatus.Failed;
    }

    public void Authorize(string paypalOrderId, string paypalOrderStatus, string authorizationId,
        string authorizationStatus, decimal amount, string currency, DateTimeOffset createdAt,
        DateTimeOffset expirationTime)
    {
        if (Status != OrderStatus.AwaitingPayment) throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        if (amount != Total()) throw new InvalidOperationException("The authorized amount does not match the order total.");
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expirationTime;
        PaymentAttempt++;
        Status = string.Equals(authorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase)
            ? OrderStatus.Authorized : OrderStatus.AwaitingPayment;
        PaymentStatus = string.Equals(authorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase)
            ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void Reauthorize(string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset expirationTime)
    {
        if (Status != OrderStatus.Authorized || amount != Total()) throw new InvalidOperationException("The order cannot be reauthorized.");
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expirationTime;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        if (string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationStatus = "CAPTURED";
            PaymentStatus = PaymentStatus.Captured;
            Status = OrderStatus.Fulfilled;
        }
        else
        {
            PaymentStatus = PaymentStatus.CapturePending;
        }
    }

    public void Cancel(string authorizationStatus)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled.");
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = AuthorizationId is null ? PaymentStatus.None : PaymentStatus.Voided;
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId, decimal amount,
        string currency, string status, DateTimeOffset createdAt)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a fulfilled order can be refunded.");
        if (CapturedAmount is null || amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");
        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, amount, currency, status, createdAt);
        _refunds.Add(refund);
        if (!string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentStatus = RefundedAmount == CapturedAmount.Value ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            Status = RefundedAmount == CapturedAmount.Value ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
            CaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        return refund;
    }

    public void RefreshRefund(PaymentRefund refund, string status)
    {
        if (!_refunds.Contains(refund)) throw new InvalidOperationException("The refund does not belong to this order.");
        refund.Refresh(status);
        if (CapturedAmount is null) return;
        if (RefundedAmount == 0)
        {
            PaymentStatus = PaymentStatus.Captured;
            Status = OrderStatus.Fulfilled;
            CaptureStatus = "COMPLETED";
        }
        else
        {
            PaymentStatus = RefundedAmount == CapturedAmount.Value ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            Status = RefundedAmount == CapturedAmount.Value ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
            CaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private Order() { }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderStatus.AwaitingPayment;
        PaymentStatus = PaymentStatus.AwaitingPayment;
        PaymentReference = $"eshop-{Guid.NewGuid():N}";
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public string EnsurePaymentReference()
    {
        PaymentReference ??= $"eshop-{Guid.NewGuid():N}";
        return PaymentReference;
    }

    public void RecordAuthorization(string currency, string payPalOrderId, string payPalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount, DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        if (decimal.Round(amount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal authorized an amount different from the order total.");
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationCreatedAt = createdAt;
        Status = OrderStatus.Authorized;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationCreatedAt = createdAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal payPalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        if (decimal.Round(capturedAmount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal captured an amount different from the order total.");
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
        PaymentStatus = PaymentStatus.Captured;
    }

    public void RecordCancellation(string authorizationStatus)
    {
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be cancelled.");
        AuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
        PaymentStatus = PaymentStatus.Voided;
    }

    public void RecordUnpaidCancellation()
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Only an order awaiting payment can be cancelled without voiding an authorization.");
        Status = OrderStatus.Cancelled;
        PaymentStatus = PaymentStatus.Voided;
    }

    public OrderRefund RecordRefund(string idempotencyKey, string payPalRefundId, string payPalStatus,
        decimal amount, DateTimeOffset createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a fulfilled order can be refunded.");
        if (CapturedAmount is null || amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the captured amount remaining.");
        var refund = new OrderRefund(idempotencyKey, payPalRefundId, payPalStatus, amount, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount == CapturedAmount.Value ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        PaymentStatus = RefundedAmount == CapturedAmount.Value ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

public enum OrderStatus { AwaitingPayment, Authorized, Fulfilled, Cancelled, PartiallyRefunded, Refunded }
public enum PaymentStatus { AwaitingPayment, Authorized, Captured, Voided, PartiallyRefunded, Refunded }

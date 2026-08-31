using System;
using System.Collections.Generic;
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
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Address ShipToAddress { get; private set; }
    public string PaymentReference { get; private set; } = string.Empty;
    public OrderPaymentState PaymentState { get; private set; } = OrderPaymentState.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PaymentFailureCode { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

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

    public string EnsurePaymentReference()
    {
        if (!string.IsNullOrWhiteSpace(PaymentReference)) return PaymentReference;
        if (Id <= 0) throw new InvalidOperationException("The order must be persisted before assigning its payment reference.");

        PaymentReference = $"eshop-order-{Id}-{Guid.NewGuid():N}";
        return PaymentReference;
    }

    public void RecordPayPalOrder(string payPalOrderId, string? status, string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
        PaymentCurrency = currency;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
    }

    public void RecordAuthorization(string authorizationId, string status,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt ??= createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        PaymentState = OrderPaymentState.Authorized;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
    }

    public void RecordCapture(string captureId, string status, decimal amount,
        decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        PaymentState = status == "COMPLETED"
            ? OrderPaymentState.Fulfilled
            : OrderPaymentState.CapturePending;
    }

    public void RecordCancellation(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentState = OrderPaymentState.Cancelled;
    }

    public void RecordPaymentActionRequired(string message)
    {
        PaymentState = OrderPaymentState.PaymentActionRequired;
        PaymentFailureCode = "PAYER_ACTION_REQUIRED";
        PaymentFailureMessage = message;
    }

    public void RecordPaymentFailure(string code, string message)
    {
        PaymentFailureCode = code;
        PaymentFailureMessage = message;
    }

    public OrderRefund AddRefund(string idempotencyKey, string providerRequestId, decimal amount)
    {
        var refund = new OrderRefund(idempotencyKey, providerRequestId, amount);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplyCompletedRefund(decimal amount)
    {
        RefundedAmount += amount;
        PaymentState = CapturedAmount.HasValue && RefundedAmount >= CapturedAmount.Value
            ? OrderPaymentState.Refunded
            : OrderPaymentState.PartiallyRefunded;
    }
}

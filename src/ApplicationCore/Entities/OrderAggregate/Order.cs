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
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        Guard.Against.Null(items, nameof(items));
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Address ShipToAddress { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string CreatePaymentRequestId { get; private set; } = string.Empty;
    public string AuthorizeRequestId { get; private set; } = string.Empty;
    public string CaptureRequestId { get; private set; } = string.Empty;
    public string VoidRequestId { get; private set; } = string.Empty;
    public string ReauthorizeRequestId { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(r => r.Status is "COMPLETED" or "PENDING")
        .Sum(r => r.Amount);

    public decimal ReservedRefundAmount => _refunds
        .Where(r => r.Status is "STARTED" or "COMPLETED" or "PENDING")
        .Sum(r => r.Amount);

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public void InitializePayment(string currency, string requestPrefix)
    {
        if (!string.IsNullOrEmpty(Currency)) return;
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency)).ToUpperInvariant();
        CreatePaymentRequestId = $"{requestPrefix}-create";
        AuthorizeRequestId = $"{requestPrefix}-authorize";
        CaptureRequestId = $"{requestPrefix}-capture";
        VoidRequestId = $"{requestPrefix}-void";
        ReauthorizeRequestId = $"{requestPrefix}-reauthorize";
    }

    public void RecordAuthorization(string payPalOrderId, string? payPalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        if (captureStatus == "COMPLETED")
        {
            PaymentStatus = OrderPaymentStatus.Fulfilled;
            FulfilledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.CapturePending;
        }
    }

    public void MarkCancelled(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund StartRefund(string idempotencyKey, string requestId, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, requestId, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    public void UpdateRefundState()
    {
        if (CapturedAmount is null) return;
        PaymentStatus = RefundedAmount >= CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : RefundedAmount > 0 ? OrderPaymentStatus.PartiallyRefunded : OrderPaymentStatus.Fulfilled;
    }
}

public enum OrderPaymentStatus
{
    AwaitingPayment,
    Authorized,
    CapturePending,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}

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
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ShipToAddress = Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        _orderItems = Guard.Against.Null(items, nameof(items));
        PaymentStatus = PaymentStatus.AwaitingPayment;
        FulfilmentStatus = FulfilmentStatus.Pending;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Guid PaymentOperationId { get; private set; } = Guid.NewGuid();
    public Address ShipToAddress { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfilmentStatus FulfilmentStatus { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems) total += item.UnitPrice * item.Units;
        return total;
    }

    public void SetCurrency(string currency) => Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
    public void EnsurePaymentOperationId()
    {
        if (PaymentOperationId == Guid.Empty) PaymentOperationId = Guid.NewGuid();
    }

    public void RecordPayPalOrder(string payPalOrderId)
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending || PaymentStatus != PaymentStatus.AwaitingPayment)
            throw new InvalidOperationException("This order cannot start another payment.");
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        OriginalAuthorizedAt ??= authorizedAt;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RecordAuthorizationStatus(string status)
    {
        PayPalAuthorizationStatus = status;
        if (status == "VOIDED") PaymentStatus = PaymentStatus.AuthorizationVoided;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? payPalFee,
        decimal? netProceeds, DateTimeOffset? capturedAt, bool amountMatchesOrder)
    {
        PayPalCaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        CapturedAmount = amount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt ?? CapturedAt;
        PaymentStatus = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
        if (status == "COMPLETED" && amountMatchesOrder) FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (FulfilmentStatus == FulfilmentStatus.Fulfilled)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        if (PaymentStatus == PaymentStatus.AwaitingPayment) PaymentStatus = PaymentStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string idempotencyKey, string refundId, string status, decimal amount)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, refundId, status, amount);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount
            ? (status == "COMPLETED" ? PaymentStatus.Refunded : PaymentStatus.RefundPending)
            : (status == "COMPLETED" ? PaymentStatus.PartiallyRefunded : PaymentStatus.RefundPending);
        return refund;
    }
}

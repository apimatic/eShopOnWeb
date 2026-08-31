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
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentCorrelationId = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Unfulfilled;
    public string PaymentCorrelationId { get; private set; } = Guid.NewGuid().ToString("N");
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationUpdatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    public decimal RefundedAmount() => _refunds.Where(x => x.ReservesCapturedFunds).Sum(x => x.Amount);

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        decimal amount, string currency, DateTimeOffset createdAt, DateTimeOffset? updatedAt,
        DateTimeOffset? expiresAt)
    {
        if (amount != Total()) throw new InvalidOperationException("PayPal authorized amount does not match the order total.");
        if (FulfilmentStatus != FulfilmentStatus.Unfulfilled) throw new InvalidOperationException("This order can no longer be paid.");
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        PaymentCurrency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationUpdatedAt = updatedAt;
        AuthorizationExpiresAt = expiresAt;
        OriginalAuthorizationCreatedAt ??= createdAt;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RefreshAuthorization(string authorizationId, string status, DateTimeOffset createdAt,
        DateTimeOffset? updatedAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationUpdatedAt = updatedAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RecordCapture(string captureId, string status, decimal amount, string currency,
        decimal? payPalFee, decimal? netProceeds, DateTimeOffset? capturedAt)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PaymentCurrency = currency;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PaymentStatus = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
    }

    public void MarkFulfilled(DateTimeOffset fulfilledAt)
    {
        if (PayPalCaptureStatus != "COMPLETED") throw new InvalidOperationException("The payment capture has not completed.");
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
        FulfilledAt = fulfilledAt;
    }

    public void MarkCancelled(DateTimeOffset cancelledAt, string? authorizationStatus = null)
    {
        if (FulfilmentStatus == FulfilmentStatus.Fulfilled) throw new InvalidOperationException("A fulfilled order cannot be cancelled.");
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        CancelledAt = cancelledAt;
        if (PayPalAuthorizationId is not null)
        {
            PayPalAuthorizationStatus = authorizationStatus ?? "VOIDED";
            PaymentStatus = PaymentStatus.Voided;
        }
    }

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        if (_refunds.Any(x => x.IdempotencyKey == refund.IdempotencyKey)) return;
        _refunds.Add(refund);
        RefreshRefundedStatus();
    }

    public void RefreshRefundedStatus()
    {
        if (CapturedAmount is null) return;
        PaymentStatus = RefundedAmount() >= CapturedAmount.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        PayPalCaptureStatus = PaymentStatus == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }
}

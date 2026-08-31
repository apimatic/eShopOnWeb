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
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
        : this(buyerId, shipToAddress, items)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency.ToUpperInvariant();
        PaymentStatus = PaymentStatus.AwaitingPayment;
        PaymentReference = $"eshop-{Guid.NewGuid():N}";
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Address ShipToAddress { get; private set; }
    public string? Currency { get; private set; }
    public string? PaymentReference { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.NotRequired;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Unfulfilled;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationRenewalCount { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public void RecordPayPalOrder(string paypalOrderId, string status)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment && PayPalOrderId != paypalOrderId)
            throw new InvalidOperationException("This order cannot start another payment.");
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits)
    {
        if (amount != Total()) throw new InvalidOperationException("The authorized amount does not equal the order total.");
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.Pending;
        PayPalOrderStatus = "COMPLETED";
    }

    public void RecordAuthorizationState(string status, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationRenewalCount++;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.Pending;
    }

    public void MarkAuthorizationExpired()
    {
        PayPalAuthorizationStatus = "EXPIRED";
        PaymentStatus = PaymentStatus.Expired;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal fee,
        decimal net, DateTimeOffset capturedAt)
    {
        if (amount != Total()) throw new InvalidOperationException("The captured amount does not equal the order total.");
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        PaymentStatus = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.Pending;
        if (status == "COMPLETED") FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus = null)
    {
        if (FulfilmentStatus == FulfilmentStatus.Fulfilled)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        if (PayPalAuthorizationId is not null)
        {
            PayPalAuthorizationStatus = authorizationStatus ?? "VOIDED";
            PaymentStatus = PaymentStatus.Voided;
        }
        else
        {
            PaymentStatus = PaymentStatus.Cancelled;
        }
    }

    public PaymentRefund AddRefund(string callerIdempotencyKey, string paypalRequestId,
        string paypalRefundId, string status, decimal amount, DateTimeOffset createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.CallerIdempotencyKey == callerIdempotencyKey);
        if (existing is not null) return existing;
        if (CapturedAmount is null || amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund would exceed the captured amount.");
        var refund = new PaymentRefund(callerIdempotencyKey, paypalRequestId, paypalRefundId, status,
            amount, Currency!, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

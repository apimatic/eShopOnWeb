using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
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
        PaymentReference = $"ESHOP-{Guid.NewGuid():N}";
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string PaymentReference { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public byte[] Version { get; private set; } = Array.Empty<byte>();

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(x => x.Status is "COMPLETED" or "PENDING")
        .Sum(x => x.Amount);

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public void RecordAuthorization(string currency, string paypalOrderId, string paypalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        if (decimal.Round(amount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal's authorized amount does not equal the order total.");

        Currency = currency;
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void UpdateAuthorizationStatus(string authorizationStatus)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can refresh its authorization status.");
        AuthorizationStatus = authorizationStatus;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? paypalFee, decimal? netProceeds, DateTimeOffset capturedAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        if (decimal.Round(amount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal's captured amount does not equal the order total.");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = captureStatus.ToUpperInvariant() switch
        {
            "COMPLETED" => PaymentStatus.Fulfilled,
            "PENDING" => PaymentStatus.CapturePending,
            _ => PaymentStatus.CaptureFailed
        };
    }

    public void UpdateCapture(string captureStatus, decimal amount, decimal? paypalFee,
        decimal? netProceeds, DateTimeOffset capturedAt)
    {
        if (PaymentStatus != PaymentStatus.CapturePending)
            throw new InvalidOperationException("This order does not have a pending capture.");
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        if (string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            PaymentStatus = PaymentStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus, DateTimeOffset cancelledAt)
    {
        if (PaymentStatus is not (PaymentStatus.AwaitingPayment or PaymentStatus.Authorized))
            throw new InvalidOperationException("Only an unfulfilled order can be cancelled.");

        if (authorizationStatus is not null) AuthorizationStatus = authorizationStatus;
        CancelledAt = cancelledAt;
        PaymentStatus = PaymentStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, DateTimeOffset createdAt)
    {
        if (PaymentStatus is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a fulfilled order can be refunded.");

        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        if (CapturedAmount is null || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund would exceed the captured amount.");

        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        RecalculateRefundState();
        return refund;
    }

    public void UpdateRefundStatus(string paypalRefundId, string status)
    {
        var refund = _refunds.Single(x => x.PayPalRefundId == paypalRefundId);
        refund.UpdateStatus(status);
        RecalculateRefundState();
    }

    private void RecalculateRefundState()
    {
        if (RefundedAmount == 0)
            PaymentStatus = PaymentStatus.Fulfilled;
        else if (RefundedAmount == CapturedAmount)
            PaymentStatus = PaymentStatus.Refunded;
        else
            PaymentStatus = PaymentStatus.PartiallyRefunded;
    }
}

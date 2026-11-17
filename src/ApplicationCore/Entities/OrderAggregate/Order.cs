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
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency, long totalAmountCents)
        : this(buyerId, shipToAddress, items)
    {
        Currency = currency;
        TotalAmountCents = totalAmountCents;
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public string PaymentIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string? Currency { get; private set; }
    public long? TotalAmountCents { get; private set; }

    public string? PaymentSourceId { get; private set; }
    public string? PaymentCardBrand { get; private set; }
    public string? PaymentCardLast4 { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public decimal? FinixFeeCents { get; private set; }
    public decimal? NetAmountCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? PaymentFailureCode { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<Refund> _refunds = new List<Refund>();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void MarkAuthorized(
        string sourceId,
        string? cardBrand,
        string? cardLast4,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiresAt,
        DateTimeOffset paidAt)
    {
        PaymentSourceId = sourceId;
        PaymentCardBrand = cardBrand;
        PaymentCardLast4 = cardLast4;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaidAt = paidAt;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
        Status = OrderStatus.Authorized;
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage)
    {
        PaymentFailureCode = failureCode;
        PaymentFailureMessage = failureMessage;
        Status = OrderStatus.PaymentFailed;
    }

    public void MarkCaptured(
        string transferId,
        string transferStatus,
        long capturedAmountCents,
        decimal finixFeeCents,
        decimal netAmountCents,
        DateTimeOffset fulfilledAt)
    {
        CaptureTransferId = transferId;
        CaptureStatus = transferStatus;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetAmountCents = netAmountCents;
        FulfilledAt = fulfilledAt;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
        Status = RefundedAmountCents > 0
            ? (RefundedAmountCents >= capturedAmountCents ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded)
            : OrderStatus.Fulfilled;
    }

    public void UpdateCaptureDetails(string transferStatus, long capturedAmountCents, decimal finixFeeCents, decimal netAmountCents)
    {
        CaptureStatus = transferStatus;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetAmountCents = netAmountCents;
    }

    public void MarkCaptureFailed(string? failureCode, string? failureMessage)
    {
        PaymentFailureCode = failureCode;
        PaymentFailureMessage = failureMessage;
        Status = OrderStatus.PaymentFailed;
    }

    public void MarkCancelled(DateTimeOffset cancelledAt)
    {
        CancelledAt = cancelledAt;
        Status = OrderStatus.Cancelled;
    }

    public Refund AddRefund(string finixRefundId, long amountCents, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        var refund = new Refund(Id, finixRefundId, amountCents, status, idempotencyKey, createdAt);
        _refunds.Add(refund);
        RefundedAmountCents += amountCents;

        if (CapturedAmountCents.HasValue && RefundedAmountCents >= CapturedAmountCents.Value)
        {
            Status = OrderStatus.Refunded;
        }
        else
        {
            Status = OrderStatus.PartiallyRefunded;
        }

        return refund;
    }

    public long RemainingRefundableCents()
    {
        var captured = CapturedAmountCents ?? TotalAmountCents ?? 0;
        var remaining = captured - RefundedAmountCents;
        return remaining < 0 ? 0 : remaining;
    }

    public long ResolveTotalAmountCents()
    {
        return TotalAmountCents ?? checked((long)decimal.Round(Total() * 100m, 0, MidpointRounding.AwayFromZero));
    }

    public bool HasRefund(string idempotencyKey)
    {
        return _refunds.Any(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public string GetOrCreatePaymentIdempotencyKey()
    {
        if (string.IsNullOrWhiteSpace(PaymentIdempotencyKey))
        {
            PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
        }

        return PaymentIdempotencyKey;
    }

    public Refund? FindRefund(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }
}

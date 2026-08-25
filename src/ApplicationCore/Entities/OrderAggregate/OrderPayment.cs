using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-owned state (hold, capture, refunds) for the payment of a single Order.
/// This is a child entity of the Order aggregate, not an aggregate root of its own.
/// </summary>
public class OrderPayment : BaseEntity
{
    public int OrderId { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public int? PaymentMethodId { get; private set; }

    public string PayPalOrderId { get; private set; } = null!;

    public string AuthorizationId { get; private set; } = null!;
    public string AuthorizationStatus { get; private set; } = null!;
    public DateTimeOffset AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that PayPal has confirmed as COMPLETED.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.IsCompleted).Sum(r => r.Amount);

    /// <summary>Sum of refunds that are COMPLETED or still PENDING - the amount no longer available to refund again.</summary>
    public decimal ConsumedForRefund => _refunds.Where(r => r.IsPendingOrCompleted).Sum(r => r.Amount);

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string currency, decimal amount, int? paymentMethodId,
        string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset authorizationCreatedAt, DateTimeOffset authorizationExpiresAt)
    {
        OrderId = orderId;
        Currency = currency;
        Amount = amount;
        PaymentMethodId = paymentMethodId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = authorizationCreatedAt;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationCount++;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? feeAmount, decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoided(DateTimeOffset voidedAt)
    {
        AuthorizationStatus = "VOIDED";
        VoidedAt = voidedAt;
    }

    public void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
    }
}

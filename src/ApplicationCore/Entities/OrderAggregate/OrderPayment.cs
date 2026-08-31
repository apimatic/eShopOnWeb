using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by EF Core
    private OrderPayment() { }
#pragma warning restore CS8618

    internal OrderPayment(int orderId, string currency, string createOrderRequestId)
    {
        OrderId = orderId;
        Currency = currency;
        CreateOrderRequestId = createOrderRequestId;
        Status = PaymentStatus.Creating;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string Currency { get; private set; }
    public string CreateOrderRequestId { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizationAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CaptureAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string paypalOrderId, string status, DateTimeOffset now)
    {
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = status;
        Status = PaymentStatus.Authorizing;
        AuthorizeRequestId ??= CreateOrderRequestId.EndsWith("-create", StringComparison.Ordinal)
            ? CreateOrderRequestId[..^7] + "-authorize"
            : CreateOrderRequestId + "-authorize";
        UpdatedAt = now;
    }

    public void RecordAuthorization(string paypalOrderStatus, string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4,
        DateTimeOffset now)
    {
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
        UpdatedAt = now;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        CaptureRequestId = null;
        Status = PaymentStatus.Authorized;
        UpdatedAt = now;
    }

    public void StartCapture(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(AuthorizationId))
        {
            throw new InvalidOperationException("The payment has no authorization to capture.");
        }
        CaptureRequestId ??= $"eshop-order-{OrderId}-capture-{AuthorizationId}";
        Status = PaymentStatus.CapturePending;
        UpdatedAt = now;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount, decimal? paypalFee,
        decimal? netAmount, DateTimeOffset? capturedAt, DateTimeOffset now)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CaptureAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? PaymentStatus.Captured
            : string.Equals(captureStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                ? PaymentStatus.CapturePending
                : PaymentStatus.Failed;
        UpdatedAt = now;
    }

    public void RecordVoid(string authorizationStatus, DateTimeOffset now)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
        UpdatedAt = now;
    }

    public PaymentRefund StartRefund(string idempotencyKey, string paypalRequestId, decimal amount, DateTimeOffset now)
    {
        var existing = _refunds.SingleOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (CaptureAmount is null || string.IsNullOrWhiteSpace(CaptureId))
        {
            throw new InvalidOperationException("The payment has not been captured.");
        }

        var reserved = _refunds
            .Where(r => r.Status != PaymentRefundStatus.Failed)
            .Sum(r => r.Amount);
        if (amount <= 0 || reserved + amount > CaptureAmount.Value)
        {
            throw new InvalidOperationException("The refund exceeds the captured amount still available.");
        }

        var refund = new PaymentRefund(idempotencyKey, paypalRequestId, amount, Currency, now);
        _refunds.Add(refund);
        return refund;
    }

    public void UpdateRefundTotals(DateTimeOffset now)
    {
        RefundedAmount = _refunds
            .Where(r => r.Status is PaymentRefundStatus.Pending or PaymentRefundStatus.Completed)
            .Sum(r => r.Amount);
        Status = RefundedAmount == 0
            ? PaymentStatus.Captured
            : CaptureAmount is not null && RefundedAmount >= CaptureAmount.Value
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
    }

    public void RecordFailure(string reason, DateTimeOffset now)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
        UpdatedAt = now;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    internal OrderPayment(string currency)
    {
        Currency = currency;
        ExternalReference = $"ESHOP-{Guid.NewGuid():N}";
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public Order? Order { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string Currency { get; private set; }
    public string ExternalReference { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationRenewalCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string id, string status, DateTimeOffset now)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
        UpdatedAt = now;
    }

    public void RecordAuthorization(string id, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, string orderStatus, DateTimeOffset now,
        bool renewed = false)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt ?? now;
        OriginalAuthorizationCreatedAt ??= AuthorizationCreatedAt;
        AuthorizationExpiresAt = expiresAt;
        PayPalOrderStatus = orderStatus;
        Status = status.ToUpperInvariant() switch
        {
            "CREATED" => PaymentStatus.Authorized,
            "PENDING" => PaymentStatus.AuthorizationPending,
            _ => PaymentStatus.Failed
        };
        if (renewed)
        {
            AuthorizationRenewalCount++;
        }
        UpdatedAt = now;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset? capturedAt, DateTimeOffset now)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt ?? now;
        Status = status.ToUpperInvariant() switch
        {
            "COMPLETED" => PaymentStatus.Captured,
            "PENDING" => PaymentStatus.CapturePending,
            "PARTIALLY_REFUNDED" => PaymentStatus.PartiallyRefunded,
            "REFUNDED" => PaymentStatus.Refunded,
            _ => PaymentStatus.Failed
        };
        UpdatedAt = now;
    }

    public void RecordVoided(string authorizationStatus, DateTimeOffset now)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
        UpdatedAt = now;
    }

    public PaymentRefund StartRefund(string idempotencyKey, decimal amount, DateTimeOffset now)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, now);
        _refunds.Add(refund);
        UpdatedAt = now;
        return refund;
    }

    public void RefreshRefundTotals(DateTimeOffset now)
    {
        RefundedAmount = _refunds
            .Where(x => string.Equals(x.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Amount);

        if (_refunds.Any(x => string.Equals(x.Status, "STARTED", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(x.Status, "PENDING", StringComparison.OrdinalIgnoreCase)))
        {
            Status = PaymentStatus.RefundPending;
        }
        else if (CapturedAmount.HasValue && RefundedAmount >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (RefundedAmount > 0)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
        UpdatedAt = now;
    }
}

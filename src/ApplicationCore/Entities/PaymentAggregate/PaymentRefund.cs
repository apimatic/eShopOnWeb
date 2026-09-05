using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment, keyed by the caller-supplied idempotency key.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key; unique per payment.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>PayPal's own refund id.</summary>
    public string? PayPalRefundId { get; private set; }

    public string Status { get; private set; } = PaymentRefundStatus.Pending;

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount, string currency,
        string? payPalRefundId, string status)
    {
        Guard.Against.NegativeOrZero(paymentId, nameof(paymentId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        PayPalRefundId = payPalRefundId;
        Status = string.IsNullOrWhiteSpace(status) ? PaymentRefundStatus.Pending : status;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Local view of PayPal refund statuses.</summary>
public static class PaymentRefundStatus
{
    public const string Completed = "COMPLETED";
    public const string Pending = "PENDING";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture.
/// Child of the <see cref="Payment"/> aggregate. Carries the caller-supplied idempotency key
/// so a repeated request under the same key never refunds twice, while two distinct partial
/// refunds of the same capture remain legitimate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public const string StatusCompleted = "COMPLETED";
    public const string StatusPending = "PENDING";

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key; unique per (payment, key).</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's refund id, once the refund has been accepted.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsCompleted => string.Equals(Status, StatusCompleted, StringComparison.OrdinalIgnoreCase);

    /// <summary>Records the result PayPal returned for this refund.</summary>
    public void SetResult(string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}

using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Part of the Payment aggregate.
/// The caller-supplied <see cref="IdempotencyKey"/> makes repeated refund requests safe: a repeat
/// under the same key returns this record instead of refunding again.
/// </summary>
public class Refund
{
    public string PayPalRefundId { get; private set; }

    /// <summary>The refunded amount, in the payment's currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's own status for the refund (e.g. COMPLETED, PENDING, CANCELLED, FAILED).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// A refund counts against the refundable balance unless PayPal has told us it will never
    /// settle (FAILED/CANCELLED). PENDING and COMPLETED both reserve the amount.
    /// </summary>
    public bool ReservesFunds =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

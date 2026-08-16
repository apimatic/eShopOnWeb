using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture. Carries the id and status
/// PayPal owns, plus the caller-supplied idempotency key so a repeated request under the same key
/// resolves to this same refund instead of refunding twice.
/// </summary>
public class Refund : BaseEntity
{
    // Refund statuses as reported by PayPal (v2 Payments API).
    public const string StatusCompleted = "COMPLETED";
    public const string StatusPending = "PENDING";
    public const string StatusCancelled = "CANCELLED";
    public const string StatusFailed = "FAILED";

    public int PaymentId { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id (v2 Payments API).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(status, nameof(status));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
    }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }

    /// <summary>
    /// Whether this refund counts against the capture's refundable balance. A cancelled or failed
    /// refund gave no money back, so it must not consume the balance.
    /// </summary>
    public bool CountsAgainstBalance =>
        !string.Equals(Status, StatusCancelled, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, StatusFailed, StringComparison.OrdinalIgnoreCase);
}

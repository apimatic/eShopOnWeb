using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single (full or partial) refund issued against a captured <see cref="Payment"/>.
/// Carries the caller-supplied idempotency key so a repeated request under the same key
/// is recognised and never refunds twice.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "PENDING";
        // A globally-unique, stable id sent to PayPal as PayPal-Request-Id. Derived from a GUID (not the
        // caller key) so it can never collide with another merchant-account request; the caller key governs
        // our own local idempotency instead.
        GatewayRequestId = Guid.NewGuid().ToString("N");
    }

    /// <summary>Caller-supplied idempotency key; unique per distinct refund attempt.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Stable, globally-unique PayPal-Request-Id for this refund's single gateway call.</summary>
    public string GatewayRequestId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's refund id, once the refund has been accepted.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal-reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordResult(string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}

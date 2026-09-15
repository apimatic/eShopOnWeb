using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured payment. Part of the Order aggregate (via <see cref="OrderPayment"/>).
/// The caller-supplied <see cref="IdempotencyKey"/> guarantees a repeated refund request does not refund twice,
/// while two distinct keys represent two legitimate partial refunds of the same capture.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string currencyCode, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        RefundId = refundId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }
    /// <summary>The caller-supplied idempotency key this refund was created under.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>When this refund was issued (used to scope reconciliation reports).</summary>
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}

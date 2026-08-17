using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture. A capture may carry several
/// distinct partial refunds. The caller-supplied <see cref="IdempotencyKey"/> makes a repeated
/// refund request a no-op while allowing genuinely different partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>PayPal's refund id (v2 payments/refunds resource).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }

    /// <summary>Caller-supplied key that de-duplicates retries of the same refund request.</summary>
    public string IdempotencyKey { get; private set; }

    public string? NoteToPayer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string currency, string status,
        string idempotencyKey, string? noteToPayer)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
        NoteToPayer = noteToPayer;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentRefundStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3
}

/// <summary>
/// A single (full or partial) refund of a captured payment. The caller-supplied
/// idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    internal PaymentRefund(string payPalRefundId, string status, decimal amount,
        string idempotencyKey, string? noteToPayer)
    {
        PayPalRefundId = payPalRefundId;
        Status = MapStatus(status);
        PayPalStatus = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        NoteToPayer = noteToPayer;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }

    /// <summary>The status exactly as reported by PayPal (COMPLETED, PENDING, ...).</summary>
    public string PayPalStatus { get; private set; }
    public PaymentRefundStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? NoteToPayer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private static PaymentRefundStatus MapStatus(string status) => status?.ToUpperInvariant() switch
    {
        "COMPLETED" => PaymentRefundStatus.Completed,
        "FAILED" => PaymentRefundStatus.Failed,
        "CANCELLED" => PaymentRefundStatus.Cancelled,
        _ => PaymentRefundStatus.Pending
    };
}

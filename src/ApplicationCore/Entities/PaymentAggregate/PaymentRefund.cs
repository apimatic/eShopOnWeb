using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. The caller-supplied idempotency key guarantees
/// that repeating the same refund request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public const string StatusPending = "PENDING";
    public const string StatusCompleted = "COMPLETED";
    public const string StatusFailed = "FAILED";
    public const string StatusCancelled = "CANCELLED";

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(string refundId, string status, decimal amount, string idempotencyKey)
    {
        RefundId = refundId;
        Status = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string RefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

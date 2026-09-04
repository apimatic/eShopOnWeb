using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A refund of part or all of the captured payment, as reported by PayPal.
/// </summary>
public class Refund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>PayPal's refund id (also the caller-facing refundId).</summary>
    public string RefundId { get; private set; } = string.Empty;

    /// <summary>Caller-supplied idempotency key that created this refund.</summary>
    public string? IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Raw PayPal refund status: COMPLETED, PENDING, FAILED, CANCELLED.</summary>
    public string Status { get; private set; } = "PENDING";

    public DateTimeOffset RequestedTime { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedTime { get; private set; }

    /// <summary>True when the refund actually moved money (completed or still pending).</summary>
    public bool IsEffective => Status is "COMPLETED" or "PENDING";

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string refundId, decimal amount, string status, DateTimeOffset? completedTime, string? idempotencyKey = null)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        CompletedTime = completedTime;
        IdempotencyKey = idempotencyKey;
    }

    public void UpdateStatus(string status, DateTimeOffset? completedTime)
    {
        Status = status;
        if (completedTime.HasValue)
            CompletedTime = completedTime;
    }

    public void SetIdempotencyKey(string key) => IdempotencyKey = key;
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single (full or partial) refund issued against a captured payment.
/// Written before the provider call (write-ahead) so a lost provider response
/// can never lead to a double refund under a retried idempotency key.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(decimal amount, string idempotencyKey)
    {
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Status = RefundStatuses.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's refund id; null until the provider confirms the refund.</summary>
    public string? PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a refund request under the same
    /// key returns this record instead of refunding again.
    /// </summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    public void Fail(string status)
    {
        Status = status;
    }

    /// <summary>Whether this record counts against the captured total.</summary>
    public bool CountsAgainstTotal() => Status != RefundStatuses.Failed;
}

public static class RefundStatuses
{
    /// <summary>Recorded locally; the provider call has not completed.</summary>
    public const string Pending = "PENDING";

    /// <summary>The provider reported the idempotency key as already used - the refund
    /// was submitted but its outcome could not be read back.</summary>
    public const string SubmittedUnknown = "SUBMITTED_UNKNOWN";

    /// <summary>The provider call failed before any refund was created.</summary>
    public const string Failed = "FAILED";
}

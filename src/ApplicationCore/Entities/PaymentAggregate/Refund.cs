namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund applied to a captured payment. Carries the caller-supplied idempotency key so a
/// repeated request under the same key returns this record instead of refunding twice, while two
/// distinct partial refunds of the same capture each get their own record.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>A refund still counts against the refundable balance unless PayPal rejected it.</summary>
    public bool CountsAgainstBalance =>
        !string.Equals(Status, "FAILED", System.StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "CANCELLED", System.StringComparison.OrdinalIgnoreCase);
}

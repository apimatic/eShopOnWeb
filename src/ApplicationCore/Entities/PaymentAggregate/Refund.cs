using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. Refunds are children of <see cref="Payment"/>.
/// The <see cref="IdempotencyKey"/> is caller-supplied so that repeating a refund request under
/// the same key never refunds twice, while two distinct partial refunds remain legitimate.
/// </summary>
public class Refund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, decimal amount, string currencyCode)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "PENDING";
    }

    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkCompleted(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    /// <summary>
    /// A refund counts against the captured total unless PayPal ultimately failed or cancelled it.
    /// </summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

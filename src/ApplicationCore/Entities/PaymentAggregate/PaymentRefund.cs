using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund against a captured payment. A refund is identified locally by the caller-supplied
/// <see cref="IdempotencyKey"/>, so replaying the same request returns this row instead of
/// refunding a second time.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }

    /// <summary>The refund status exactly as the processor reported it (e.g. <c>COMPLETED</c>).</summary>
    public string Status { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount, string currencyCode)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    /// <summary>
    /// A refund the processor cancelled or failed never returned money, so it must not count against
    /// what is still refundable.
    /// </summary>
    public bool ReducesRefundableBalance =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund against an order's captured payment. The caller-supplied <see cref="IdempotencyKey"/>
/// is unique so a repeated request under the same key never refunds twice, while two distinct partial
/// refunds remain legitimate.
/// </summary>
public class RefundRecord : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private RefundRecord() { }
#pragma warning restore CS8618

    public RefundRecord(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public int OrderPaymentId { get; private set; }
}

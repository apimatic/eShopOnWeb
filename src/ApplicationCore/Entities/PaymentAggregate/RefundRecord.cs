using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund issued against a captured payment. <see cref="IdempotencyKey"/> is
/// the caller-supplied key; it is unique per order so repeating a request under
/// the same key returns the original refund instead of issuing a second one.
/// </summary>
public class RefundRecord : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private RefundRecord() {}

    public RefundRecord(int orderId, string idempotencyKey, string finixRefundTransferId, long amountCents, string? finixState = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(finixRefundTransferId, nameof(finixRefundTransferId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));

        OrderId = orderId;
        IdempotencyKey = idempotencyKey;
        FinixRefundTransferId = finixRefundTransferId;
        AmountCents = amountCents;
        FinixState = finixState;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string FinixRefundTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string? FinixState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A refund (Finix reversal) issued against a captured payment.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(long amountCents, string idempotencyKey, string? finixRefundTransferId,
        string? finixState, DateTimeOffset createdAt)
    {
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        FinixRefundTransferId = finixRefundTransferId;
        FinixState = finixState;
        CreatedAt = createdAt;
    }

    public int OrderPaymentId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FinixRefundTransferId { get; private set; }
    public string? FinixState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateFinixState(string? state)
    {
        FinixState = state;
    }
}

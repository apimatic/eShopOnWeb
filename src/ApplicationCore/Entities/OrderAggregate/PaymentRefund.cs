using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId, string finixReversalId, string finixState, int amountCents, string idempotencyKey)
    {
        OrderPaymentId = orderPaymentId;
        FinixReversalId = finixReversalId;
        FinixState = finixState;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderPaymentId { get; private set; }
    public string FinixReversalId { get; private set; }
    public string FinixState { get; private set; }
    public int AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void SetState(string state) => FinixState = state;
}

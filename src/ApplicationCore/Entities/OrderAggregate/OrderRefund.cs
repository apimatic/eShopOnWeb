using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618
    private OrderRefund() { }
    #pragma warning restore CS8618

    public OrderRefund(int orderId, long amountCents, string idempotencyKey, string finixRefundTransferId, string finixRefundState)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderId = orderId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        FinixRefundTransferId = finixRefundTransferId;
        FinixRefundState = finixRefundState;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string FinixRefundTransferId { get; private set; }
    public string FinixRefundState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateFinixState(string state)
    {
        if (!string.IsNullOrWhiteSpace(state))
        {
            FinixRefundState = state;
        }
    }
}

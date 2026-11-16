using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId, string finixTransferId, decimal amount, string idempotencyKey,
        string? state, string? type)
    {
        Guard.Against.NegativeOrZero(orderPaymentId, nameof(orderPaymentId));
        Guard.Against.NullOrEmpty(finixTransferId, nameof(finixTransferId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderPaymentId = orderPaymentId;
        FinixTransferId = finixTransferId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        State = state;
        Type = type;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string FinixTransferId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? State { get; private set; }
    public string? Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateState(string? state) => State = state;
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Refund : BaseEntity
{
    private Refund() { }

    public Refund(int paymentId, string idempotencyKey, long amountCents)
    {
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        State = RefundState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixState { get; private set; }
    public RefundState State { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void MarkSucceeded(string finixTransferId, string? finixState)
    {
        FinixTransferId = finixTransferId;
        FinixState = finixState;
        State = RefundState.Succeeded;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? failureCode, string? failureMessage)
    {
        State = RefundState.Failed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPending(string finixTransferId, string? finixState)
    {
        FinixTransferId = finixTransferId;
        FinixState = finixState;
        State = RefundState.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

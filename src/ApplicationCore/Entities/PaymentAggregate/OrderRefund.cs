using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund (Finix transfer reversal) requested against a captured order payment.
/// </summary>
public class OrderRefund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, long amountCents, string idempotencyKey)
    {
        OrderId = orderId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixTransferState { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkSucceeded(string finixTransferId, string? finixTransferState)
    {
        FinixTransferId = finixTransferId;
        FinixTransferState = finixTransferState;
        Status = "Succeeded";
        FailureMessage = null;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string message)
    {
        Status = "Failed";
        FailureMessage = message;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

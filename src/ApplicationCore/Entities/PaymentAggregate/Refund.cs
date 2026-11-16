using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix reversal transfer) against a captured payment.
/// </summary>
public class Refund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(int paymentId, long amountCents, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(paymentId, nameof(paymentId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PaymentId = paymentId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        Status = RefundStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int PaymentId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? ProviderTransferId { get; private set; }
    public string? ProviderState { get; private set; }
    public RefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkPending()
    {
        Status = RefundStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSucceeded(string providerTransferId, string providerState)
    {
        ProviderTransferId = providerTransferId;
        ProviderState = providerState;
        Status = RefundStatus.Succeeded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string providerState)
    {
        ProviderState = providerState;
        Status = RefundStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

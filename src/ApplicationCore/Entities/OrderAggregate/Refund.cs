using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(int orderId, int orderPaymentId, string idempotencyKey, long amountCents)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(orderPaymentId, nameof(orderPaymentId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));

        OrderId = orderId;
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string? FinixRefundTransferId { get; private set; }
    public string? State { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordGatewayResult(string transferId, string? state)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));
        FinixRefundTransferId = transferId;
        State = state;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void RecordFailure(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

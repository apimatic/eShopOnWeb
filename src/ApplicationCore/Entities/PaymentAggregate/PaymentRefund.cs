using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix calls these reversals) against a captured payment.
/// The caller supplied idempotency key is stored with the refund so repeating
/// the same request cannot return money twice.
/// </summary>
public class PaymentRefund : BaseEntity, IAggregateRoot
{
    // Required by Entity Framework.
    private PaymentRefund()
    {
    }

    public PaymentRefund(int orderPaymentId, int orderId, string buyerId, long amountInCents,
        string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(orderPaymentId, nameof(orderPaymentId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amountInCents, nameof(amountInCents));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderPaymentId = orderPaymentId;
        OrderId = orderId;
        BuyerId = buyerId;
        AmountInCents = amountInCents;
        IdempotencyKey = idempotencyKey;
        Status = RefundStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderPaymentId { get; private set; }

    public int OrderId { get; private set; }

    public string BuyerId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public long AmountInCents { get; private set; }

    public string? FinixRefundTransferId { get; private set; }

    public string? FinixState { get; private set; }

    public RefundStatus Status { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkSucceeded(string finixRefundTransferId, string? state)
    {
        Guard.Against.NullOrEmpty(finixRefundTransferId, nameof(finixRefundTransferId));

        FinixRefundTransferId = finixRefundTransferId;
        FinixState = state;
        Status = RefundStatus.Succeeded;
        LastError = null;
        Touch();
    }

    public void MarkFailed(string error)
    {
        Status = RefundStatus.Failed;
        LastError = error;
        Touch();
    }

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}

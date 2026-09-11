using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. Part of the <see cref="Payment"/> aggregate.
/// The caller-supplied <see cref="IdempotencyKey"/> makes repeat requests safe: the same key
/// never refunds twice, while two distinct keys are two legitimate partial refunds.
/// </summary>
public class Refund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = RefundStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public string? PayPalRefundId { get; private set; }

    public RefundStatus Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public void MarkCompleted(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = RefundStatus.Completed;
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
    }
}

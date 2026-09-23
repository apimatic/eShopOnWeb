using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. The <see cref="IdempotencyKey"/> is the caller-supplied
/// key; a unique index on (OrderPaymentId, IdempotencyKey) makes a replay of the same key reject the
/// second insert, so a repeated refund request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key. Also sent to PayPal as PayPal-Request-Id.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal's refund id. Null until the provider call returns.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal refund status (COMPLETED / PENDING / FAILED / CANCELLED). Null until it returns.</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>True once the provider confirmed a non-failed refund and the amount counts against the capture.</summary>
    public bool IsEffective { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId, string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
    }

    public void RecordResult(string payPalRefundId, string status, bool isEffective)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        IsEffective = isEffective;
    }
}

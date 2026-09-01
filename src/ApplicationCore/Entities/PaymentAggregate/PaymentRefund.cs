using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single (full or partial) refund against a captured payment. The caller-supplied
/// idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public const string RefundStatusFailed = "FAILED";

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    internal PaymentRefund(int orderPaymentId, string idempotencyKey, string payPalRefundId, string status,
        decimal amount, string currency, string? note)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        Note = note;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

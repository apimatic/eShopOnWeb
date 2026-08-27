using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Records a single (full or partial) refund issued against an order's captured payment.
/// The idempotency key is supplied by the caller; repeating a refund request under the
/// same key returns this record instead of refunding again.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(string payPalRefundId, decimal amount, string currency, string idempotencyKey, string status, string? note)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Note = note;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Status { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}

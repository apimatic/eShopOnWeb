using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a <see cref="Payment"/>'s capture. A capture may have
/// several refunds (distinct partial refunds). Each refund carries the caller-supplied
/// idempotency key so a repeated request under the same key is never applied twice.
/// </summary>
public class Refund : BaseEntity
{
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string idempotencyKey, string payPalRefundId, decimal amount, string currencyCode, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(status, nameof(status));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
    }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }
}

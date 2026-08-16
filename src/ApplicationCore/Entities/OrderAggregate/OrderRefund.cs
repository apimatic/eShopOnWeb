using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment of an order. Part of the Order aggregate
/// (owned through <see cref="OrderPayment"/>); never an aggregate root of its own.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string payPalRefundId, decimal amount, string currencyCode, string status, string idempotencyKey)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own identifier for this refund (used for later look-ups / reconciliation).</summary>
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public string Status { get; private set; }

    /// <summary>Caller-supplied key that makes the refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

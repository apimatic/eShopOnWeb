using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against an order's capture. Belongs to the <see cref="OrderPayment"/> aggregate.
/// Carries the caller-supplied idempotency key so a repeat of the same request is not refunded twice,
/// while two distinct partial refunds of the same capture remain separate rows.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string idempotencyKey, decimal amount, string currency, string payPalRefundId, string status)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.Now;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key — unique per logical refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal's own refund id, so a later request can act on it.</summary>
    public string PayPalRefundId { get; private set; }

    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

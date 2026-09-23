using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund made against an order's captured payment. Belongs to an <see cref="OrderPayment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request a
/// no-op; it is unique across all refunds.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key; unique across refunds.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The amount refunded, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal's refund status (COMPLETED, PENDING, FAILED, CANCELLED).</summary>
    public string Status { get; private set; }

    /// <summary>PayPal-generated refund id, once the call returns.</summary>
    public string? PayPalRefundId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordResult(string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    /// <summary>A refund counts against the captured total unless PayPal actively rejected it.</summary>
    public bool CountsAgainstCapturedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

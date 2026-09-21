using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against an <see cref="OrderPayment"/>'s captured payment. Carries the caller-supplied
/// idempotency key so a repeated request under the same key does not refund twice, while two distinct
/// partial refunds of the same capture remain legitimate.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        State = RefundState.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>The caller-supplied idempotency key. Unique per <see cref="OrderPayment"/>.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>PayPal's own id for this refund; null until PayPal has acknowledged it.</summary>
    public string? PayPalRefundId { get; private set; }

    public RefundState State { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Record the outcome PayPal reported for this refund.</summary>
    public void Settle(string? payPalRefundId, RefundState state)
    {
        if (!string.IsNullOrWhiteSpace(payPalRefundId))
        {
            PayPalRefundId = payPalRefundId;
        }
        State = state;
    }

    /// <summary>Whether this refund counts against the captured amount (anything not failed/cancelled).</summary>
    public bool CountsTowardRefundedTotal => State is RefundState.Pending or RefundState.Completed;
}

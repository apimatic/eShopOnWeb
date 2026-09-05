using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single return of captured money to the shopper, made under a caller-supplied idempotency key.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public RefundStatus Status { get; private set; }

    /// <summary>The id PayPal gave the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>Caller-supplied key that makes a repeated refund request replay rather than refund twice.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset Requested { get; private set; }
    public DateTimeOffset? Completed { get; private set; }

    /// <summary>Fee PayPal gave back with the refund, when it reported one.</summary>
    public decimal? FeeReturned { get; private set; }

    /// <summary>Net reduction of the merchant's proceeds for this refund.</summary>
    public decimal? NetAmount { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618 // Required by Entity Framework

    public PaymentRefund(decimal amount, string currency, string idempotencyKey, DateTimeOffset requested)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NotAllowed(amount <= 0m, "A refund must be for more than zero.");

        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        Requested = requested;
        Status = RefundStatus.Pending;
    }

    public void MarkCompleted(string payPalRefundId, decimal? feeReturned, decimal? netAmount, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        PayPalRefundId = payPalRefundId;
        FeeReturned = feeReturned;
        NetAmount = netAmount;
        Status = RefundStatus.Completed;
        Completed = now;
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
    }
}

using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Owned collection item: one refund against the order's capture. The caller-supplied
/// idempotency key is stored so a replayed request returns the recorded refund instead
/// of issuing a second one.
/// </summary>
public class PaymentRefund
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey,
        string providerRefundId,
        string providerCaptureId,
        decimal amount,
        string currencyCode,
        string status,
        decimal? totalRefundedAmount,
        string refundReference)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(providerRefundId, nameof(providerRefundId));

        IdempotencyKey = idempotencyKey;
        ProviderRefundId = providerRefundId;
        ProviderCaptureId = providerCaptureId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        TotalRefundedAmount = totalRefundedAmount;
        RefundReference = refundReference;
        RefundedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public string ProviderRefundId { get; private set; }
    public string ProviderCaptureId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public string Status { get; private set; }
    public decimal? TotalRefundedAmount { get; private set; }

    /// <summary>The unique provider invoice reference used for this refund (correlates the settled provider row).</summary>
    public string RefundReference { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }

    /// <summary>Failed/cancelled refunds do not consume the captured amount.</summary>
    public bool ConsumesCaptureAmount =>
        Status != RefundStatuses.Failed && Status != RefundStatuses.Cancelled;

    public void UpdateStatus(string status, decimal? totalRefundedAmount)
    {
        Status = status;
        if (totalRefundedAmount.HasValue)
        {
            TotalRefundedAmount = totalRefundedAmount;
        }
    }
}

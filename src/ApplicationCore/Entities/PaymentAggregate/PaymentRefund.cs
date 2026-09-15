using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public sealed class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(int paymentRecordId, string idempotencyKey, decimal requestedAmount)
    {
        PaymentRecordId = paymentRecordId;
        IdempotencyKey = idempotencyKey;
        RequestedAmount = requestedAmount;
    }

    public int PaymentRecordId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public decimal RequestedAmount { get; private set; }
    public string State { get; private set; } = "Pending";
    public string? PayPalRefundId { get; private set; }
    public string? PayPalStatus { get; private set; }
    public decimal? RefundedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProviderCreatedAt { get; private set; }

    public void Complete(string providerId, string? status, decimal amount, DateTimeOffset? providerCreatedAt)
    {
        PayPalRefundId = providerId;
        PayPalStatus = status;
        RefundedAmount = amount;
        ProviderCreatedAt = providerCreatedAt ?? DateTimeOffset.UtcNow;
        State = "Completed";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? status)
    {
        PayPalStatus = status;
        State = "Failed";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

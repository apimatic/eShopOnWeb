using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(decimal amount, string currency, string idempotencyKey, string payPalRequestId)
    {
        Amount = amount;
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        PayPalRequestId = Guard.Against.NullOrEmpty(payPalRequestId, nameof(payPalRequestId));
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRequestId { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal void Complete(string payPalRefundId, string status, decimal amount, DateTimeOffset? completedAt)
    {
        if (amount != Amount)
            throw new InvalidOperationException($"PayPal refunded {amount:F2}, but {Amount:F2} was requested.");

        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
    }
}

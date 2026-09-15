using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool CountsAgainstCapturedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

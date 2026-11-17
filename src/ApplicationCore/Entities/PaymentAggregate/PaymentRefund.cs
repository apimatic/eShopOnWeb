using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(long amountCents, string idempotencyKey, string? finixRefundId, string status)
    {
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        FinixRefundId = finixRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FinixRefundId { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateFromFinix(string? finixRefundId, string status)
    {
        FinixRefundId = finixRefundId;
        Status = status;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "STARTED";
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordResult(string id, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset now)
    {
        PayPalRefundId = id;
        Status = status;
        Amount = amount;
        PayPalFee = fee;
        NetAmount = net;
        UpdatedAt = now;
    }

    public void RecordFailure(DateTimeOffset now)
    {
        Status = "FAILED";
        UpdatedAt = now;
    }
}

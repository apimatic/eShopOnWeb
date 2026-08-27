using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentRefundStatus
{
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// A refund (full or partial) issued against the captured payment of an order.
/// The caller-supplied idempotency key guarantees a retried request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status, string? noteToPayer)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        NoteToPayer = noteToPayer;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string? NoteToPayer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetStatus(string status)
    {
        Status = status;
    }
}

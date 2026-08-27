using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount, string currency, string? noteToPayer)
    {
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        NoteToPayer = noteToPayer;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key returns this record.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? NoteToPayer { get; private set; }

    /// <summary>PayPal's refund id, assigned once PayPal accepts the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>Status as reported by PayPal (COMPLETED, PENDING, FAILED, CANCELLED).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkSettled(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}

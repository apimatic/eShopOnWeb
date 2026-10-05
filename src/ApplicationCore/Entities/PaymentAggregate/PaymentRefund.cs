using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, decimal amount, string providerRequestId, DateTimeOffset requestedAt)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        ProviderRequestId = providerRequestId;
        RequestedAt = requestedAt;
        Status = PaymentRefundStatus.Pending;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentRefundStatus Status { get; private set; }
    /// <summary>Idempotency key sent to the provider (PayPal-Request-Id); replayed to settle an unknown outcome.</summary>
    public string ProviderRequestId { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Counts against the refundable balance unless it definitively failed.</summary>
    public bool ReservesFunds => Status != PaymentRefundStatus.Failed;

    internal void Record(string providerRefundId, string? providerStatus, bool completed, bool failed, DateTimeOffset now)
    {
        ProviderRefundId = providerRefundId;
        ProviderStatus = providerStatus;
        if (failed)
        {
            Status = PaymentRefundStatus.Failed;
            FailureReason = $"PayPal reported the refund as {providerStatus}.";
        }
        else if (completed)
        {
            Status = PaymentRefundStatus.Completed;
            CompletedAt = now;
        }
        else
        {
            Status = PaymentRefundStatus.Pending;
        }
    }

    internal void Fail(string reason)
    {
        Status = PaymentRefundStatus.Failed;
        FailureReason = reason;
    }
}

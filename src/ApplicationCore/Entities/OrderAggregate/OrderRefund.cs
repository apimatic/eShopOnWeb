using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderPaymentId,
        long amountCents,
        string idempotencyKey,
        string? finixTransferId,
        string status,
        string? failureCode = null,
        string? failureMessage = null)
    {
        OrderPaymentId = orderPaymentId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        FinixTransferId = finixTransferId;
        Status = status;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderPaymentId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string Status { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool CountsTowardsRefundedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELED", StringComparison.OrdinalIgnoreCase);

    public void UpdateStatus(string status, string? failureCode, string? failureMessage)
    {
        Status = status;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

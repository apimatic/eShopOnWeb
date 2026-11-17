using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix Transfer Reversal) against a captured payment.
/// </summary>
public class PaymentRefund : BaseEntity, IAggregateRoot
{
    private PaymentRefund() { }

    public PaymentRefund(int orderPaymentId, long amountMinor, string? finixRefundId,
        string status, string idempotencyKey, string? failureCode, string? failureMessage)
    {
        OrderPaymentId = orderPaymentId;
        AmountMinor = amountMinor;
        FinixRefundId = finixRefundId;
        Status = status;
        IdempotencyKey = idempotencyKey;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public long AmountMinor { get; private set; }
    public string? FinixRefundId { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool CountsAgainstCapturedAmount =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);

    public void UpdateFromGateway(string status, string? failureCode, string? failureMessage)
    {
        Status = status;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
    }
}

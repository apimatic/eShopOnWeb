using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string paypalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        PayPalRefundId = paypalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool CountsAgainstCapturedAmount =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

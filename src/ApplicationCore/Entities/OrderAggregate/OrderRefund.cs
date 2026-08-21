using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string refundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));

        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
    }

    public int OrderPaymentId { get; private set; }
    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }
}

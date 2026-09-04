using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }
    public PaymentMethod(string buyerId, string vaultId, string brand, string last4, string expiry) { BuyerId = buyerId; VaultId = vaultId; Brand = brand; Last4 = last4; Expiry = expiry; }
    public string BuyerId { get; private set; } = string.Empty;
    public string VaultId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    public void Delete() => IsDeleted = true;
}

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }
    public PaymentRefund(int paymentId, string idempotencyKey, decimal amount) { PaymentId = paymentId; IdempotencyKey = idempotencyKey; Amount = amount; Status = "Pending"; }
    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public void Completed(string refundId, string status) { RefundId = refundId; Status = status; }
}

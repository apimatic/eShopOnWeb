using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentAuthorization : BaseEntity
{
    private PaymentAuthorization() { }

    public PaymentAuthorization(string id, string status, decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = id;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public int PaymentId { get; private set; }
    public string PayPalAuthorizationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public void UpdateStatus(string status) => Status = status;
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentAuthorization : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentAuthorization() { }
#pragma warning restore CS8618

    internal PaymentAuthorization(string paypalAuthorizationId, string status, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, bool isCurrent)
    {
        PayPalAuthorizationId = paypalAuthorizationId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsCurrent = isCurrent;
    }

    public string PayPalAuthorizationId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsCurrent { get; private set; }

    internal void MakeHistorical() => IsCurrent = false;
    internal void UpdateStatus(string status) => Status = status;
}

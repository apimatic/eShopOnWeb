using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentAuthorization : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentAuthorization() { }
#pragma warning restore CS8618

    public PaymentAuthorization(
        string payPalAuthorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        DateTimeOffset expirationTime)
    {
        PayPalAuthorizationId = Guard.Against.NullOrWhiteSpace(payPalAuthorizationId, nameof(payPalAuthorizationId));
        Status = Guard.Against.NullOrWhiteSpace(status, nameof(status));
        Amount = amount;
        Currency = Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        CreatedAt = createdAt;
        ExpirationTime = expirationTime;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalAuthorizationId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpirationTime { get; private set; }
    public bool IsCurrent { get; private set; } = true;

    public void Supersede()
    {
        IsCurrent = false;
        Status = "REAUTHORIZED";
    }

    public void MarkCaptured()
    {
        Status = "CAPTURED";
    }

    public void UpdateStatus(string status)
    {
        Status = Guard.Against.NullOrWhiteSpace(status, nameof(status));
    }
}

using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string setupRequestId, string tokenRequestId)
    {
        BuyerId = buyerId;
        SetupRequestId = setupRequestId;
        TokenRequestId = tokenRequestId;
    }

    public string BuyerId { get; private set; }
    public string SetupRequestId { get; private set; }
    public string TokenRequestId { get; private set; }
    public string? PayPalSetupTokenId { get; private set; }
    public string? PayPalPaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public string Status { get; private set; } = "PENDING";
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }

    public void RecordSetupToken(string setupTokenId, string status)
    {
        PayPalSetupTokenId = setupTokenId;
        Status = status;
    }

    public void Activate(string paymentTokenId, string? customerId, string? brand,
        string? lastDigits, string? expiry, string? cardholderName, string status)
    {
        PayPalPaymentTokenId = paymentTokenId;
        PayPalCustomerId = customerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        Status = status;
    }

    public void MarkFailed() => Status = "FAILED";

    public void Delete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        Status = "DELETED";
    }
}

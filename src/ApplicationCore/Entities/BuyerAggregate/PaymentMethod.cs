using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    internal PaymentMethod(string requestId)
    {
        RequestId = requestId;
        Status = PaymentMethodStatus.Pending;
    }

    public string RequestId { get; private set; }
    public string? PayPalPaymentTokenId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public PaymentMethodStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; private set; }
    public bool IsActive => Status == PaymentMethodStatus.Active;

    internal void Complete(string payPalPaymentTokenId, string brand, string last4, string expiry, string? cardholderName)
    {
        PayPalPaymentTokenId = payPalPaymentTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        Status = PaymentMethodStatus.Active;
    }

    internal void Remove()
    {
        Status = PaymentMethodStatus.Removed;
        RemovedAt = DateTimeOffset.UtcNow;
    }
}

public enum PaymentMethodStatus
{
    Pending,
    Active,
    Removed
}

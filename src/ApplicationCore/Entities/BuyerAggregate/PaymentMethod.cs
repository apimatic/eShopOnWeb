using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    internal PaymentMethod(string payPalVaultId, string brand, string lastDigits,
        string? expiry, DateTimeOffset createdAt)
    {
        PayPalVaultId = payPalVaultId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CreatedAt = createdAt;
    }

    public int BuyerId { get; private set; }
    public Buyer? Buyer { get; private set; }
    public string PayPalVaultId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void Delete(DateTimeOffset deletedAt) => DeletedAt = deletedAt;
}

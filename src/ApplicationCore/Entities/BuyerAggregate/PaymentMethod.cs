namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    internal PaymentMethod(string vaultId, string customerId, string brand, string last4,
        string expiry, string? alias)
    {
        VaultId = vaultId;
        CustomerId = customerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }

    public string? Alias { get; private set; }
    public string VaultId { get; private set; }
    public string CustomerId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public string Expiry { get; private set; }
}

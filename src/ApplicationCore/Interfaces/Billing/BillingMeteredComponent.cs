namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class BillingMeteredComponent
{
    public BillingMeteredComponent(int id, string handle, bool isMetered, long pricePerUnitInCents)
    {
        Id = id;
        Handle = handle;
        IsMetered = isMetered;
        PricePerUnitInCents = pricePerUnitInCents;
    }

    public int Id { get; }
    public string Handle { get; }
    public bool IsMetered { get; }
    public long PricePerUnitInCents { get; }
    public decimal PricePerUnit => PricePerUnitInCents / 100m;
}

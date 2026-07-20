namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class BillingProductFamily
{
    public BillingProductFamily(int id, string handle, string? name)
    {
        Id = id;
        Handle = handle;
        Name = name;
    }

    public int Id { get; }
    public string Handle { get; }
    public string? Name { get; }
}

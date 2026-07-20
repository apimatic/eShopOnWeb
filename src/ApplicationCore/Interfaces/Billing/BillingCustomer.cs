namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string? email)
    {
        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }
    public string? Reference { get; }
    public string? Email { get; }
}

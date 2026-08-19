namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// Identity of an eShopOnWeb shopper as projected into Maxio.
/// </summary>
public sealed class ShopperIdentity
{
    public ShopperIdentity(string email, string firstName, string lastName)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}

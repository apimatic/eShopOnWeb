namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A Maxio customer mapped to an eShopOnWeb identity user via <see cref="Reference"/>.
/// </summary>
public sealed class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string email, string firstName, string lastName)
    {
        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; }
    public string? Reference { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }
}

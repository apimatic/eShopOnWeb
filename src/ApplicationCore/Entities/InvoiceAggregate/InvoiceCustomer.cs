using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

/// <summary>
/// The customer details a bill carries. A value object owned by <see cref="Invoice"/>; these are the
/// details a shopper is allowed to correct while the bill is still a draft.
/// </summary>
public class InvoiceCustomer // ValueObject
{
    public string Name { get; private set; }
    public string Email { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private InvoiceCustomer() { }
#pragma warning restore CS8618

    public InvoiceCustomer(string name, string email)
    {
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.NullOrEmpty(email, nameof(email));

        Name = name;
        Email = email;
    }
}

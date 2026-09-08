namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A Maxio customer record.
/// </summary>
public class MaxioCustomer
{
    public MaxioCustomer(int id, string? reference, string? email)
    {
        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }
    public string? Reference { get; }
    public string? Email { get; }
}

using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity on whose behalf a billing operation is performed.
/// <para>
/// UserName is the stable key that ties an eShopOnWeb account to its billing-system customer
/// record, so repeated calls always resolve to the same customer.
/// </para>
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}

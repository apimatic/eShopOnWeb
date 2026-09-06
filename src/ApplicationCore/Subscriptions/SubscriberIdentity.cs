using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user a billing operation acts on behalf of. Always derived from the caller
/// authenticated identity, never from request input.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email.Trim();
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
    }

    /// <summary>
    /// The eShopOnWeb user name. This is the durable business key the billing-system customer is
    /// keyed on, so the link survives a rebuild of the local identity store.
    /// </summary>
    public string UserName { get; }

    /// <summary>Email address to register with the billing system.</summary>
    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}

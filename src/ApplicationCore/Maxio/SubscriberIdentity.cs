namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The authenticated eShopOnWeb shopper requesting a subscription.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userId, string email, string? firstName = null, string? lastName = null)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable Identity user id (GUID); basis of the customer reference.</summary>
    public string UserId { get; }

    public string Email { get; }

    /// <summary>Optional; derived from the email local part when not supplied.</summary>
    public string? FirstName { get; }

    /// <summary>Optional; a generic surname is used when not supplied.</summary>
    public string? LastName { get; }
}

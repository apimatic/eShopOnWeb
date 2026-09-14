namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper being enrolled. <see cref="Reference"/> is the stable external key
/// used to correlate the eShop user with a Maxio customer (so enrolment is idempotent per user).
/// </summary>
public class SubscriberInfo
{
    public SubscriberInfo(string reference, string email)
    {
        Reference = reference;
        Email = email;
    }

    /// <summary>Stable eShop-side identifier (the user's login/username). Used as the Maxio customer reference.</summary>
    public string Reference { get; }

    public string Email { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identity of the eShopOnWeb shopper being enrolled, used to find-or-create the matching
/// Maxio customer. <see cref="Reference"/> is the idempotency key: the stable eShopOnWeb user id
/// is stored as the Maxio customer's <c>reference</c>, so re-subscribing never creates a second
/// customer.
/// </summary>
public class SubscriberInfo
{
    /// <summary>Stable eShopOnWeb user id. Persisted as the Maxio customer reference.</summary>
    public string Reference { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;
}

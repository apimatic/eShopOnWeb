namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The subset of the eShopOnWeb user profile needed to enroll them in Maxio. The reference is
/// the stable user identifier used as the Maxio customer reference and as the lookup key for
/// "my subscriptions"; it must be deterministically derivable from the JWT on every call.
/// </summary>
public sealed record SubscriptionIdentity(
    string UserName,
    string Email,
    string FirstName,
    string LastName);

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation is performed. The
/// <see cref="Reference"/> is the stable link between an eShopOnWeb identity and
/// its customer record in the billing system, which makes "ensure customer"
/// idempotent across process restarts (the local store is in-memory and resets,
/// but the billing system persists the customer keyed by this reference).
/// </summary>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName);

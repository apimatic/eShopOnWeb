namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb shopper on whose behalf a billing operation is performed.
/// <para>
/// <see cref="Email"/> is the stable identity: it is what the JWT carries, and it survives host
/// restarts even when the identity store is the in-memory provider (which regenerates user ids).
/// The billing implementation derives its provider-side customer reference from it.
/// </para>
/// </summary>
/// <param name="Email">The shopper's e-mail address, taken from the authenticated principal.</param>
public readonly record struct SubscriberIdentity(string Email);

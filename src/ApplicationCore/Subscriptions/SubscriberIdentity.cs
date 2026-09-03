namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation runs. Derived by the API layer from the
/// authenticated caller's JWT — never from request input — so the billing account can never be
/// spoofed by the request body.
/// </summary>
/// <param name="UserName">
/// The eShop user name (an email in this app). Stable per user; used as the Maxio customer
/// <c>reference</c>, which is the idempotency anchor tying an eShop user to exactly one Maxio customer.
/// </param>
/// <param name="Email">The user's email address (the customer contact email in Maxio).</param>
public readonly record struct SubscriberIdentity(string UserName, string Email);

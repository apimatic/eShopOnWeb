namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Who is asking. Customers may only act on subscriptions whose provider-side customer reference
/// matches their eShopOnWeb identity; administrators may act on any subscription.
/// </summary>
/// <param name="UserReference">
/// The eShopOnWeb user name (email) used as the provider-side customer reference — see plan §4.4.
/// </param>
public sealed record SubscriptionActor(string UserReference, bool IsAdministrator);

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subset of eShopOnWeb user data needed to enroll a Maxio customer.
/// </summary>
public record SubscriptionUser(string Id, string Email, string FirstName, string LastName);

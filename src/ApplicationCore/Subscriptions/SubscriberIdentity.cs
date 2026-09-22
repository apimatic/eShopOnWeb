namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated caller, resolved from the JWT at the API boundary. <see cref="UserId"/> is stable
/// per user and is used as the Maxio customer <c>reference</c> (which Maxio enforces unique per site).
/// </summary>
public record SubscriberIdentity(string UserId, string Email, string FirstName, string LastName);

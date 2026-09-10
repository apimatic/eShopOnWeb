namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity used to ensure a matching Maxio customer exists.
/// <paramref name="Reference"/> is the stable, unique application user id and is used as the
/// Maxio customer <c>reference</c> for idempotency.
/// </summary>
public record CustomerRegistration(string Reference, string Email, string FirstName, string LastName);

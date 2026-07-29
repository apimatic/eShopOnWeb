namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user that a Maxio customer/subscription is created for. Built from the
/// authenticated caller's claims (never from request input), so the billing account is always
/// tied to the identity in the JWT.
/// </summary>
/// <param name="Reference">
/// Stable, unique-per-user key stored on the Maxio customer's <c>reference</c> field. It is what
/// makes "ensure a customer exists" idempotent: a repeat request resolves the same customer.
/// </param>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName);

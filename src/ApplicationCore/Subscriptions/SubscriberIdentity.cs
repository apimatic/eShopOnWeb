namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper being enrolled in billing. <see cref="Reference"/> is the
/// stable idempotency key that ties an eShopOnWeb user to exactly one Maxio customer.
/// </summary>
/// <param name="Reference">
/// Stable, unique identifier of the eShopOnWeb user (its user name / login). Stored as the
/// Maxio customer <c>reference</c> so the same user always maps to the same customer.
/// </param>
/// <param name="Email">The shopper's email address, required by Maxio to create a customer.</param>
/// <param name="FirstName">Optional given name used when first creating the Maxio customer.</param>
/// <param name="LastName">Optional family name used when first creating the Maxio customer.</param>
public record SubscriberIdentity(string Reference, string Email, string? FirstName, string? LastName);

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user for whom a Maxio customer/subscription is managed. <see cref="Reference"/> is the
/// stable, per-user key used as the Maxio customer <c>reference</c>, so the same user always maps to the
/// same Maxio customer regardless of local (in-memory) database resets — Maxio is the record of truth.
/// </summary>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName);

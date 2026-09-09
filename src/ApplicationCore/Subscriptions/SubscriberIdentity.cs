namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user, expressed in the terms Maxio needs to identify (or create) a billing
/// customer. <see cref="Reference"/> is the stable, unique per-user key used both as the Maxio
/// customer <c>reference</c> and to guard against duplicate customers/subscriptions. All values
/// are derived server-side from the caller's authenticated identity, never from request input.
/// </summary>
public record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName);

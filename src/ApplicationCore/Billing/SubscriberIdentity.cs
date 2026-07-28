namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The stable identity of an eShopOnWeb shopper as seen by the billing system.
/// <see cref="Reference"/> is the idempotency key written to (and looked up from) the Maxio
/// customer <c>reference</c>; it must be the same value on every call for a given user so a
/// double-click never creates a second customer. For eShopOnWeb the username (an email) is used
/// for both the reference and the email.
/// </summary>
public record SubscriberIdentity(string Reference, string Email);

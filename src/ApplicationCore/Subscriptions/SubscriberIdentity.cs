namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a billing operation is performed on behalf of.
/// The <see cref="Reference"/> is the stable key that is stored on the Maxio customer
/// record (as its <c>reference</c>) so the eShop user ↔ Maxio customer mapping lives in
/// Maxio and survives application restarts (important here because the in-memory database
/// loses all state between runs).
/// </summary>
public record SubscriberIdentity(
    string Reference,
    string Email,
    string? FirstName = null,
    string? LastName = null);

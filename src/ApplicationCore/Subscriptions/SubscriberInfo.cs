namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a billing operation acts on behalf of.
/// <see cref="Reference"/> is the application-owned, stable key used as the Maxio customer
/// reference so that find-before-create stays idempotent across process restarts (Maxio, not
/// the in-memory database, is the system of record for the mapping).
/// </summary>
public record SubscriberInfo(string Reference, string Email, string FirstName, string LastName);

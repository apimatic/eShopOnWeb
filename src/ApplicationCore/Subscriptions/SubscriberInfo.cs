namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies an eShopOnWeb user to the billing system of record.
/// <see cref="Reference"/> is the stable, unique key this application owns for the user and is used
/// as the billing customer's reference so the same user always maps to the same billing customer
/// (making customer provisioning idempotent).
/// </summary>
public record SubscriberInfo(string Reference, string Email, string? FirstName, string? LastName);

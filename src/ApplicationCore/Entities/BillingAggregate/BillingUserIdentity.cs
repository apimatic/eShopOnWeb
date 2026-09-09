namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// The identity of an eShopOnWeb user as it is projected into the billing system of record.
/// <see cref="Reference"/> is the stable, unique external key used to locate (or create) the
/// matching Maxio customer idempotently; <see cref="Email"/> is the contact email recorded on
/// that customer. For eShopOnWeb both are the user's login name (an email address).
/// </summary>
public sealed record BillingUserIdentity(string Reference, string Email);

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's customer record, keyed by the eShopOnWeb user reference (§4.4).
/// </summary>
public sealed record BillingCustomer(int Id, string Reference, string Email);

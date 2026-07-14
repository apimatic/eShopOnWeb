namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The billing provider's customer record, keyed on eShopOnWeb's stable user reference.</summary>
public record BillingCustomer(int Id, string Reference, string Email);

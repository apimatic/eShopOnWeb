namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>The authenticated shopper the billing customer is created for. Never taken from a request body.</summary>
public sealed record BillingCustomerProfile(string UserName, string Email);

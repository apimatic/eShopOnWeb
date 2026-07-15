namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A provider-agnostic view of the billing-provider's customer record.</summary>
public record BillingCustomer(int Id, string Reference);

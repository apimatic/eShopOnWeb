namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An amount together with its ISO currency code.</summary>
public record Money(decimal Amount, string Currency);

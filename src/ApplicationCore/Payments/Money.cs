namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A monetary amount in a single currency. Currency is USD for this integration.</summary>
public record Money(decimal Amount, string CurrencyCode = "USD");

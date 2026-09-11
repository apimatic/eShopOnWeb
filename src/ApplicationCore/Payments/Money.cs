namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A monetary amount tied to an ISO-4217 currency. Mirrors PayPal's `money` schema
/// (currency_code + value) but keeps the value as a <see cref="decimal"/> internally so the
/// application never loses precision; the gateway is responsible for formatting to the string
/// representation the PayPal contract requires.
/// </summary>
public record Money(string CurrencyCode, decimal Amount);

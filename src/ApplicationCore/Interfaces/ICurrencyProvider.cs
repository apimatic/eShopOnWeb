namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Supplies the ISO-4217 currency payments are taken in. The value comes from configuration
/// (<c>PayPal:Currency</c>) and is never hard-coded, so the same build can run against a different
/// PayPal account and currency.
/// </summary>
public interface ICurrencyProvider
{
    string CurrencyCode { get; }
}

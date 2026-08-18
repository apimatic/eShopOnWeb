namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment settings ApplicationCore needs, kept as a small abstraction so the core has no
/// dependency on the options/config packages. Implemented over the bound PayPal settings.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code for amounts, from configuration (PayPal:Currency).</summary>
    string CurrencyCode { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment configuration the application layer needs without depending on the PayPal SDK/Infrastructure.</summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code used for all amounts (from PayPal:Currency).</summary>
    string CurrencyCode { get; }
}

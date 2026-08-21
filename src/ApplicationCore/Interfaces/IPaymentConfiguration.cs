namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The payment settings the application layer needs; the processor currency comes from configuration.</summary>
public interface IPaymentConfiguration
{
    /// <summary>3-letter ISO currency code the order total is charged in (e.g. "USD").</summary>
    string CurrencyCode { get; }
}

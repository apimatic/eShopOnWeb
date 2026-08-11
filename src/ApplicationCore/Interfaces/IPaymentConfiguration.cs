namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Exposes payment settings the application core needs, sourced from configuration.</summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code the merchant charges in (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}

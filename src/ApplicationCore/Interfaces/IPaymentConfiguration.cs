namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The payment settings the domain needs, sourced from configuration in Infrastructure.</summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code for all payments (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}

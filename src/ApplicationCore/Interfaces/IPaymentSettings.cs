namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application layer needs. Concretely bound from the <c>PayPal:</c> configuration
/// section in the host; kept as an abstraction so ApplicationCore does not depend on the options plumbing.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code used for all amounts (from <c>PayPal:Currency</c>).</summary>
    string CurrencyCode { get; }
}

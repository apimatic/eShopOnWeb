namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the domain services need. Backed by the bound <c>PayPal:</c>
/// configuration section; the concrete values live only in configuration/user-secrets.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code all amounts are charged in (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}

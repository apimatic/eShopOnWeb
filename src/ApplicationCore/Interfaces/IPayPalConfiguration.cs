namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Exposes the PayPal settings the domain/services need without depending on the concrete
/// settings type or the configuration provider. Bound from the <c>PayPal:</c> configuration section.
/// </summary>
public interface IPayPalConfiguration
{
    /// <summary>ISO-4217 currency for all amounts (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}

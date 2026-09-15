namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subset of PayPal configuration the domain services need. Bound from the <c>PayPal:</c>
/// configuration section in the host; never hard-coded.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>The ISO-4217 currency orders are priced and charged in (from <c>PayPal:Currency</c>).</summary>
    string Currency { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the domain needs, without depending on how they are bound. The currency
/// comes from configuration (never hard-coded).
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code every order is priced and charged in.</summary>
    string Currency { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Exposes the payment configuration the domain needs (currently the currency for new orders),
/// so ApplicationCore does not have to depend on the infrastructure-level PayPal settings.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>The ISO-4217 currency code every new order is priced and charged in.</summary>
    string Currency { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment configuration the domain needs, kept free of the concrete PayPal settings type.
/// The currency comes from configuration (PayPal:Currency); amounts come from catalog prices.
/// </summary>
public interface IPaymentConfiguration
{
    string Currency { get; }
}

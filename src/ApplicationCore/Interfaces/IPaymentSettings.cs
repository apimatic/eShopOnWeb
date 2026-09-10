namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The payment settings the application layer needs. The currency is sourced from configuration
/// (never hard-coded) and applied to every amount sent to PayPal.
/// </summary>
public interface IPaymentSettings
{
    string Currency { get; }
}

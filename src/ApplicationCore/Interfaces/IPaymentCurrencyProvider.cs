namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Supplies the currency all payment operations are denominated in (from configuration).
/// </summary>
public interface IPaymentCurrencyProvider
{
    string Currency { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Supplies the configured payment currency to the application layer without leaking the
/// infrastructure's options types. Backed by the <c>PayPal:Currency</c> setting.
/// </summary>
public interface IPaymentCurrencyProvider
{
    string Currency { get; }
}

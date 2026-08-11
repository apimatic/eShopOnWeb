namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application layer needs, without depending on the Infrastructure binding.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code for all amounts (from PayPal:Currency configuration).</summary>
    string Currency { get; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Bound from the PayPal configuration section. Values come from environment / user-secrets, never from source.
/// </summary>
public interface IPayPalSettings
{
    string Currency { get; }
}

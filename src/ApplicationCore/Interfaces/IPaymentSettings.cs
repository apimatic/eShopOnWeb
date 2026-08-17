namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Payment configuration the application layer needs. The currency for all payments
/// comes from configuration (PayPal:Currency), never hard-coded.</summary>
public interface IPaymentSettings
{
    string Currency { get; }
}

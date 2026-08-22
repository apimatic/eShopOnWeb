namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentSettings
{
    string Currency { get; }
    bool IsConfigured { get; }
}

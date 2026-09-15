namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public interface IPayPalSettings
{
    string Currency { get; }
    string ResolveBaseUrl();
}

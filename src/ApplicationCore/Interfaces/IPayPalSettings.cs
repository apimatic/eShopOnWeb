namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalSettings
{
    string Currency { get; }
    string Environment { get; }
}

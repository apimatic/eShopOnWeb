namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentSettings
{
    string Currency { get; }
    string Environment { get; }
    string BaseUrl { get; }
}

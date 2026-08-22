namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPayPalConfiguration
{
    string Currency { get; }
    string ClientId { get; }
    string ClientSecret { get; }
    string Environment { get; }
    string? BaseUrl { get; }
}

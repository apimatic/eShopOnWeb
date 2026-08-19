namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSettings
{
    string ApiKey { get; }
    string Subdomain { get; }
    string ProductFamilyHandle { get; }
    string? BaseUrl { get; }
}

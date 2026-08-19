using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions : IMaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseAddress(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return string.Empty;
        }

        return NormalizeBaseAddress($"https://{Subdomain.Trim()}.chargify.com");
    }

    internal static string NormalizeBaseAddress(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return trimmed + "/";
    }
}

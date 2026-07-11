using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "US";
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }
    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }
    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }
    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    public string ResolveBaseUrl()
    {
        // Explicit BaseUrl override always wins
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        // Fallback: derive from Subdomain (and region if not US)
        Guard.Against.NullOrWhiteSpace(Subdomain, nameof(Subdomain), "Subdomain must be configured when BaseUrl is not set");

        var regionPath = Environment.Equals("EU", StringComparison.OrdinalIgnoreCase) ? "eu" : "com";
        return $"https://{Subdomain}.chargify.{regionPath}";
    }
}

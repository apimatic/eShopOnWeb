using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");
        }

        // These are the production servers defined by maxio-spec/openapi.yaml.
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var hostSuffix = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? ".ebilling.maxio.com"
            : ".chargify.com";

        return $"https://{Subdomain}{hostSuffix}";
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

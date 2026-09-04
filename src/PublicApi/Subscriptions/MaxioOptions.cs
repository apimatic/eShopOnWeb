namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? Environment { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + '/', UriKind.Absolute);
        }

        // The client is constructed during application startup. Keep construction lazy so
        // the existing API can still start when billing is not configured; endpoint calls
        // validate all required settings before making a provider request.
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return new Uri("https://invalid.maxio.local/", UriKind.Absolute);
        }

        var host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{Subdomain}.ebilling.maxio.com/"
            : $"https://{Subdomain}.chargify.com/";

        return new Uri(host, UriKind.Absolute);
    }

    public void ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");
        }

        _ = GetBaseAddress();
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}

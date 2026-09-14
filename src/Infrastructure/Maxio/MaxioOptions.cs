namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string ConfigurationSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public bool IsConfigured
    {
        get
        {
            bool hasAuthentication = !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
            bool hasAddress = !string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain);
            return hasAuthentication && hasAddress;
        }
    }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new System.InvalidOperationException("Maxio:Subdomain or Maxio:BaseUrl must be configured before using subscription features.");
        }

        string environment = System.Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? string.Empty;
        string hostTemplate = string.Equals(environment, "EU", System.StringComparison.OrdinalIgnoreCase)
            ? "https://{0}.ebilling.maxio.com"
            : "https://{0}.chargify.com";
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, hostTemplate, Subdomain);
    }
}

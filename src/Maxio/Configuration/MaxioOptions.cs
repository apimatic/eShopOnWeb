using System.Collections.Generic;

namespace Microsoft.eShopWeb.Maxio.Configuration;

/// <summary>
/// Settings that configure how this application talks to a Maxio Advanced Billing site.
/// Bound from the "Maxio" configuration section. Values are intentionally never
/// hard-coded here; they come from configuration (environment variables, user secrets).
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (Basic auth username). See the MAXIO_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Subdomain of the Maxio Advanced Billing site. See the MAXIO_SITE_SUBDOMAIN environment variable.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds this application's subscription plans.
    /// See the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Hosting region of the Maxio site: "US" or "EU". See the MAXIO_ENVIRONMENT environment variable.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim instead of deriving
    /// one from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Validates the settings and returns a list of human readable problems. An empty list
    /// means the settings are usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"Maxio:{nameof(ApiKey)} is not configured (set MAXIO_API_KEY).");
        }

        bool hasBaseUrl = !string.IsNullOrWhiteSpace(BaseUrl);

        if (!hasBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                errors.Add($"Maxio:{nameof(Subdomain)} is not configured (set MAXIO_SITE_SUBDOMAIN or Maxio:{nameof(BaseUrl)}).");
            }

            if (!TryGetHostForEnvironment(Environment, out _))
            {
                errors.Add($"Maxio:{nameof(Environment)} value '{Environment}' is not supported. Supported values: US, EU. Set Maxio:{nameof(BaseUrl)} to use a custom API base URL.");
            }
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"Maxio:{nameof(ProductFamilyHandle)} is not configured (set MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        return errors;
    }

    /// <summary>
    /// Resolves the API base URL (no trailing slash) to use for every Maxio request.
    /// Throws <see cref="MaxioConfigurationException"/> when the settings are incomplete.
    /// </summary>
    public string GetApiBaseUrl()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new MaxioConfigurationException(string.Join(" ", errors));
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (TryGetHostForEnvironment(Environment, out var host))
        {
            return $"https://{Subdomain.Trim().ToLowerInvariant()}.{host}";
        }

        throw new MaxioConfigurationException(
            $"Maxio:{nameof(Environment)} value '{Environment}' is not supported. Set Maxio:{nameof(BaseUrl)} to use a custom API base URL.");
    }

    private static bool TryGetHostForEnvironment(string environment, out string host)
    {
        host = string.Empty;
        switch (environment?.Trim().ToUpperInvariant())
        {
            case "US":
                host = "chargify.com";
                return true;
            case "EU":
                host = "ebilling.maxio.com";
                return true;
            default:
                return false;
        }
    }
}

using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing (Billing API) integration.
/// Bound from the "Maxio" configuration section. Values are supplied per-environment
/// (user secrets / environment variables) and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Billing API key, used as the Basic-auth username (password is the literal "X").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Billing API site subdomain (e.g. "acme" for https://acme.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the product family that contains the subscription plans (products)
    /// offered to eShopOnWeb shoppers.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base URL. When set it is used verbatim as the API
    /// base address; otherwise the base URL is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the configured <see cref="BaseUrl"/> when present,
    /// otherwise the standard US Billing API host derived from the subdomain.
    /// </summary>
    public static Uri ResolveBaseUrl(MaxioOptions options)
    {
        var baseUrl = options.BaseUrl?.Trim();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            return new Uri(baseUrl.TrimEnd('/') + "/");
        }

        var subdomain = options.Subdomain?.Trim();
        if (string.IsNullOrEmpty(subdomain))
        {
            throw MaxioConfigurationException.MissingSettings("Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        return new Uri($"https://{subdomain}.chargify.com/");
    }
}

/// <summary>
/// Thrown when the Maxio integration is invoked but its required settings were not supplied.
/// </summary>
public class MaxioConfigurationException : InvalidOperationException
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }

    public static MaxioConfigurationException MissingSettings(string details) =>
        new($"Maxio billing is not configured. {details} " +
            $"Set the 'Maxio:ApiKey', 'Maxio:Subdomain' and 'Maxio:ProductFamilyHandle' configuration keys " +
            "(e.g. via user secrets or the MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY environment variables).");
}
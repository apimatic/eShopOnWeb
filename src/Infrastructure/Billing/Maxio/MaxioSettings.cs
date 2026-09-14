using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Binds the <c>Maxio</c> configuration section. Values come from user secrets or the environment
/// in development and from the platform's secret store in production - never from a file in the
/// repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Site API key, used as the user name of HTTP Basic auth.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Billing site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set it is used as the API base address exactly as given, instead of
    /// deriving one from <see cref="Subdomain"/> - which is how you point the same build at a
    /// non-US region or at a gateway host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long to wait for a single API call. The provider itself cuts requests off at 120s.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Total attempts for a retryable call, including the first one.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How long the plan catalog is reused between calls. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>Host template used when only a subdomain is supplied.</summary>
    private const string DefaultHostFormat = "https://{0}.chargify.com/";

    public bool IsConfigured => Problems().Count == 0;

    /// <summary>True when nothing at all has been supplied, i.e. the capability is simply switched off.</summary>
    public bool IsAbsent =>
        string.IsNullOrWhiteSpace(ApiKey) &&
        string.IsNullOrWhiteSpace(Subdomain) &&
        string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>Human-readable list of what is missing or malformed. Never contains secret values.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"{ConfigurationSectionName}:ApiKey is not set.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"{ConfigurationSectionName}:ProductFamilyHandle is not set.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add($"Either {ConfigurationSectionName}:Subdomain or {ConfigurationSectionName}:BaseUrl must be set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(NormalizeBaseUrl(BaseUrl!), UriKind.Absolute, out _))
        {
            problems.Add($"{ConfigurationSectionName}:BaseUrl is not an absolute URL.");
        }

        if (TimeoutSeconds <= 0)
        {
            problems.Add($"{ConfigurationSectionName}:TimeoutSeconds must be greater than zero.");
        }

        if (MaxAttempts < 1)
        {
            problems.Add($"{ConfigurationSectionName}:MaxAttempts must be at least 1.");
        }

        return problems;
    }

    /// <summary>The address every request is made relative to.</summary>
    public Uri ResolveBaseAddress()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, DefaultHostFormat, Subdomain!.Trim())
            : BaseUrl!;

        return new Uri(NormalizeBaseUrl(baseUrl), UriKind.Absolute);
    }

    /// <summary>
    /// Relative request paths only compose correctly against a base address that ends in a slash,
    /// so we add one if it is missing. Nothing else about the configured value is touched.
    /// </summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();

        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/";
    }
}

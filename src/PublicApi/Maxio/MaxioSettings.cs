using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio:" configuration section. Values come from user-secrets or
/// environment variables (Maxio__ApiKey ...), never from files in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public static MaxioSettings Load(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SECTION_NAME).Get<MaxioSettings>() ?? new MaxioSettings();
        settings.Validate();
        return settings;
    }

    /// <summary>Refuses to start when a required part of the credential is missing or blank.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Configuration value 'Maxio:ApiKey' is missing or blank.");
        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Configuration value 'Maxio:Subdomain' is missing or blank.");
        if (string.IsNullOrWhiteSpace(Environment))
            throw new InvalidOperationException("Configuration value 'Maxio:Environment' is missing or blank.");
        if (!string.Equals(Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configuration value 'Maxio:Environment' must be 'US' or 'EU'.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Configuration value 'Maxio:BaseUrl' must be an absolute URL when set.");
    }
}

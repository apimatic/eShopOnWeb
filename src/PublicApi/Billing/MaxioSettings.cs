using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Settings read from the "Maxio:" configuration section.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Reads the section and throws (so the host refuses to start) when a required setting is missing or blank.</summary>
    public static MaxioSettings Load(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SECTION).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with user-secrets or the environment.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it with user-secrets or the environment.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            throw new InvalidOperationException("Maxio:Environment is not configured (US or EU).");
        var env = settings.Environment.Trim();
        if (!string.Equals(env, "US", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(env, "EU", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Maxio:Environment must be US or EU.");
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) && !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl, when set, must be an absolute URL.");

        return settings;
    }
}

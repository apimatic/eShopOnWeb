using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Bound from the "Maxio" configuration section (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment, Maxio:BaseUrl).
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    public bool IsEu => Environment!.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads and validates the settings; throws (so the host refuses to start) when one is missing.</summary>
    public static MaxioSettings LoadAndValidate(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it with dotnet user-secrets or the Maxio__ApiKey environment variable.");
        if (string.IsNullOrWhiteSpace(settings.Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it with dotnet user-secrets or the Maxio__Subdomain environment variable.");
        if (string.IsNullOrWhiteSpace(settings.Environment)
            || !(settings.Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase)
                 || settings.Environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Maxio:Environment is not configured or is not US/EU. Set it with dotnet user-secrets or the Maxio__Environment environment variable.");
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl) && !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl is set but is not an absolute URL.");

        return settings;
    }
}

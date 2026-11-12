using System;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings of the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// The values come from configuration only (user-secrets / environment) and are never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio:ApiKey — the Maxio API key (Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio:Subdomain — the Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Maxio:Environment — "US" or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Maxio:BaseUrl — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The SDK environment selected by <see cref="Environment"/>, or null when it is not US / EU.</summary>
    public ServerEnvironment? ResolveEnvironment()
    {
        var value = Environment?.Trim();
        if (string.Equals(value, "US", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Us;
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase)) return ServerEnvironment.Eu;
        return null;
    }

    /// <summary>Returns the name of the first missing/invalid setting (never its value), or null when valid.</summary>
    public string? FirstProblem()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return $"{SectionName}:{nameof(ApiKey)} is not configured";
        if (string.IsNullOrWhiteSpace(Subdomain)) return $"{SectionName}:{nameof(Subdomain)} is not configured";
        if (string.IsNullOrWhiteSpace(Environment)) return $"{SectionName}:{nameof(Environment)} is not configured";
        if (ResolveEnvironment() is null) return $"{SectionName}:{nameof(Environment)} must be US or EU";
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && !BaseUrl.Contains("{site}"))
            return $"{SectionName}:{nameof(BaseUrl)} is not a valid absolute URL";
        return null;
    }
}

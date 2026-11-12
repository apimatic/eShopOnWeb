using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Bound from the "Maxio:" configuration section. No values are defined in the repository.</summary>
public class MaxioSettings
{
    public const string SECTION = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Throws (so the host refuses to start) when a required setting is missing or invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or the environment.");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set it via user-secrets or the environment.");
        if (!IsEu && !string.Equals(Environment?.Trim(), "US", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Maxio:Environment must be 'US' or 'EU'.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL when set.");
    }

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

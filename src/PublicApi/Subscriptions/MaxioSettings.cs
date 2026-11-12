using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (secret; user-secrets / environment only).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain; used to derive the API address unless BaseUrl is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override; used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional: plan used when a subscribe request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Fails fast (at startup) when a required value is missing. Never echoes values.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured (set it via user-secrets or the Maxio__ApiKey environment variable).");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured (or set Maxio:BaseUrl).");
    }
}

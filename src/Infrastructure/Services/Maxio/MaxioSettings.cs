using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>Bound from the "Maxio" configuration section. Secrets come from user-secrets / environment, never the repo.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio:ApiKey</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio:Subdomain - the Maxio site.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Maxio:ProductFamilyHandle - the product family whose products are the offered plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Maxio:BaseUrl - optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio:Environment - optional "US" (default) or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Maxio:DefaultPlanHandle - optional plan used when a subscribe request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Maxio:RequestBudgetSeconds - optional total budget of one endpoint call (default 30).</summary>
    public int RequestBudgetSeconds { get; set; } = 30;

    public TimeSpan RequestBudget => TimeSpan.FromSeconds(RequestBudgetSeconds);

    /// <summary>Throws (naming the key, never the value) when the settings are unusable.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or environment.");
        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Maxio:BaseUrl is set but is not an absolute URL.");
        if (!string.IsNullOrWhiteSpace(Environment) && !IsEu && !Environment.Equals("US", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Maxio:Environment must be US or EU.");
        if (RequestBudgetSeconds < 1)
            throw new InvalidOperationException("Maxio:RequestBudgetSeconds must be positive.");
    }

    public bool IsEu => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);
}

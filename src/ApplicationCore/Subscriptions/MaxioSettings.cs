namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (secret). Never store in source control; use user-secrets or environment.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "my-site".</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional Maxio hosting environment ("US" or "EU"). Defaults to US.</summary>
    public string? Environment { get; set; }

    /// <summary>Timeout applied to every Maxio call, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

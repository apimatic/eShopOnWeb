namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings of the <c>Maxio:</c> configuration section. No value is hard-coded; the sandbox values are
/// loaded from user-secrets (or environment variables) and never stored in the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>The Maxio (Chargify) API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The site subdomain, used to derive the API address when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The hosting environment of the site: US or EU.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}

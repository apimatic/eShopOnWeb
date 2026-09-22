namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio:</c> configuration section. Values are never
/// stored in the repository — they come from user-secrets / environment variables at runtime.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>The Maxio (Chargify) API key — the Basic-auth username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain; substituted into the Production base URL when no <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The API handle of the product family whose products are exposed as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional verbatim base-URL override; when set it is used as-is instead of deriving from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

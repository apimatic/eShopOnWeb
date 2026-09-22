namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> section.
/// Values are supplied via .NET user-secrets / environment variables and never committed.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Chargify API key (the Basic-auth username; the password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain; substituted into the Production/US base URL template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The API handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving the
    /// base URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}

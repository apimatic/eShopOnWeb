namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section. Values come from user-secrets in
/// development and from the environment (or a secret store) in deployments — never from appsettings files.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio Advanced Billing API key (secret).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain; used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address instead of the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region of the site, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    public bool IsEuRegion =>
        string.Equals(Environment?.Trim(), "EU", System.StringComparison.OrdinalIgnoreCase);
}

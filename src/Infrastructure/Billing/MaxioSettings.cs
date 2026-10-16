namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from user-secrets in development and from
/// environment variables (<c>Maxio__ApiKey</c>, …) or a secret store elsewhere — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (sent as the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain; required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — handle of the product family whose products are the plans on offer.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address instead of the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }
}

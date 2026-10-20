namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from user-secrets, environment
/// variables (<c>Maxio__ApiKey</c>, ...) or a secret store, never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary><c>Maxio:ApiKey</c>: the Advanced Billing API key, sent as the Basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c>: the site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c>: the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c>: optional. When set, used verbatim as the API base address instead of the subdomain.</summary>
    public string? BaseUrl { get; set; }
}

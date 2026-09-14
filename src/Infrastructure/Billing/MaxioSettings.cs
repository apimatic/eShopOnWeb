namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Binds the <c>Maxio:</c> configuration section. No value here is ever hard-coded or committed:
/// on a developer machine the secrets come from .NET user-secrets, and in a deployed environment
/// from the host's configuration provider.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section these settings bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site API key. Sent as the HTTP Basic username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// <c>Maxio:Subdomain</c> — the Maxio site subdomain. Expanded into the provider's default
    /// base-URL template; ignored when <see cref="BaseUrl"/> is supplied without a placeholder.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// <c>Maxio:ProductFamilyHandle</c> — the handle of the product family whose products are the
    /// purchasable plans. Handles are stable across catalog re-seeds; numeric ids are not, so the
    /// integration never persists an id from configuration.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional. When set, it is used verbatim as the API base address instead
    /// of deriving one from <see cref="Subdomain"/>. Also the way to target a non-US Maxio site.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// <c>Maxio:Currency</c> — optional ISO code used to present plan prices. The provider's product
    /// model carries no currency, so this is the only place one can come from. Defaults to USD.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// True when enough is configured to talk to a Maxio site. A base address can come from either
    /// <see cref="Subdomain"/> or <see cref="BaseUrl"/>.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>Describes what is missing, for a startup log line. Never includes a secret value.</summary>
    public string DescribeMissing()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"{CONFIG_SECTION}:{nameof(ApiKey)}");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"{CONFIG_SECTION}:{nameof(ProductFamilyHandle)}");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            missing.Add($"{CONFIG_SECTION}:{nameof(Subdomain)} (or {CONFIG_SECTION}:{nameof(BaseUrl)})");
        return string.Join(", ", missing);
    }
}

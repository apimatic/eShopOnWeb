using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Everything needed to talk to Maxio Advanced Billing, bound from the <c>Maxio:</c> configuration section.
/// </summary>
/// <remarks>
/// No value here is ever hard-coded. In development the credentials come from .NET user-secrets
/// (<c>dotnet user-secrets set "Maxio:ApiKey" ...</c>); elsewhere they come from whatever configuration
/// provider the deployment uses — environment variables named <c>Maxio__ApiKey</c>, a key vault, and so on.
/// </remarks>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio API key. Sent as the Basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. <c>cp-exp-2</c>. A sandbox is a site, not an environment.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional. When set it is used verbatim as the API base address instead of one derived from
    /// <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional hosting region: <c>US</c> (default) or <c>EU</c>. Anything else — including a value such as
    /// "sandbox" — falls back to US, because the SDK models only these two regions and a sandbox is
    /// expressed by <see cref="Subdomain"/>.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional. Forces the payment collection method used when enrolling a shopper — one of
    /// <c>remittance</c>, <c>invoice</c>, <c>automatic</c> or <c>prepaid</c>. Left unset, it is derived from
    /// the site: Relationship Invoicing sites get <c>remittance</c>, legacy Statements sites <c>invoice</c>.
    /// Set it to <c>automatic</c> in a deployment that does capture cards.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Bound on a single HTTP attempt. Also used as the SDK's per-attempt retry timeout.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Bound on a whole logical call, retries and backoff included.</summary>
    public int CallBudgetSeconds { get; set; } = 30;

    /// <summary>
    /// Retry attempts after the first. Cannot be lowered below 1 — the SDK's retry pipeline rejects 0.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>How long the plan catalog and site metadata are cached for.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>Logs each outbound request and its status at Debug. Off by default.</summary>
    public bool LogRequests { get; set; }

    /// <summary>
    /// Returns the configuration keys that are missing. Key <em>names</em> only — never values, which are
    /// secrets.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
            missing.Add($"{ConfigurationSection}:{nameof(ApiKey)}");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            missing.Add($"{ConfigurationSection}:{nameof(ProductFamilyHandle)}");

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            missing.Add($"{ConfigurationSection}:{nameof(Subdomain)} (or {ConfigurationSection}:{nameof(BaseUrl)})");

        return missing;
    }

    public bool IsConfigured => Validate().Count == 0;
}

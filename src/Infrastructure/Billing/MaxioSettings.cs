using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Everything the Maxio Advanced Billing integration is configured with, bound from the <c>Maxio</c>
/// configuration section. No value here has a hard-coded default that points at a particular Maxio site or
/// catalog — the same build runs against any site.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio API key. Sent as the basic-auth user name. Never committed — supply via user-secrets or the environment.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, substituted into the SDK's base-URL template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim base-address override. When set it is used exactly as given, in place of the address
    /// derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are published as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Maxio server environment: <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Bounds a single HTTP attempt at the transport. Backstop for a hung provider.</summary>
    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>Bounds a single SDK attempt, inside the SDK's own retry pipeline.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Bounds a whole call — every attempt plus all backoff. The only true call budget.</summary>
    public int CallBudgetSeconds { get; set; } = 30;

    /// <summary>
    /// Extra attempts the SDK may make. One is the floor the SDK's retry pipeline accepts; writes are
    /// additionally protected from re-sends by <see cref="MaxioHttpDiagnosticsHandler"/>.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>How long a resolved product-family id and the site currency stay cached.</summary>
    public int CatalogCacheSeconds { get; set; } = 300;

    /// <summary>Logs every outbound Maxio request and its status at Debug level. Off by default.</summary>
    public bool LogRequests { get; set; }

    /// <summary>Returns one message per configuration problem; an empty list means the integration is usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{ConfigurationSection}:{nameof(ApiKey)}' is not set.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{ConfigurationSection}:{nameof(ProductFamilyHandle)}' is not set.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add($"Either '{ConfigurationSection}:{nameof(Subdomain)}' or '{ConfigurationSection}:{nameof(BaseUrl)}' must be set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add($"'{ConfigurationSection}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(Environment)
            && !Environment.Equals("US", StringComparison.OrdinalIgnoreCase)
            && !Environment.Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'{ConfigurationSection}:{nameof(Environment)}' must be 'US' or 'EU'.");
        }

        return problems;
    }
}

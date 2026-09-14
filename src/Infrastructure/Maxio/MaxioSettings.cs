using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Everything the Maxio Advanced Billing integration needs, bound from the <c>Maxio</c> configuration
/// section. Credentials are supplied by configuration providers (user-secrets, environment, key vault) —
/// no value here is ever compiled in.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the Basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, used to derive the API host when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim API base address. When set it is used exactly as given and the subdomain is
    /// ignored — the escape hatch for a proxy, a gateway, or a mock server.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional handle of the plan to use when a subscribe request does not name one. When unset, a
    /// product family holding exactly one plan still subscribes without a handle; otherwise the caller
    /// must name the plan.
    /// </summary>
    public string? DefaultProductHandle { get; set; }

    /// <summary>
    /// Optional override for how the provider collects the subscription balance. Leave unset to derive it
    /// from the site's billing architecture, which is what a correctly configured site wants.
    /// Accepted values are the provider's own: <c>automatic</c>, <c>remittance</c>, <c>prepaid</c>, <c>invoice</c>.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Prefix for the provider-side customer reference. Keeps this deployment's customers distinguishable
    /// from anything else sharing the same Maxio site.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>Bound on a single HTTP attempt. Applied both to the SDK retry policy and to the HttpClient.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Bound on a whole logical operation, including retries and the several provider calls a subscribe
    /// takes. This is the only limit a caller actually experiences.
    /// </summary>
    public int CallBudgetSeconds { get; set; } = 45;

    /// <summary>
    /// Retry attempts after the first. The provider's own floor is 1, so writes are additionally protected
    /// by a send guard rather than by turning retries off.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>How long the site's billing currency is cached for.</summary>
    public int SiteCacheMinutes { get; set; } = 10;

    /// <summary>Logs every provider request/response line at Information. Diagnostic aid; off by default.</summary>
    public bool LogRequests { get; set; }

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 1, 300));

    public TimeSpan CallBudget => TimeSpan.FromSeconds(Math.Clamp(CallBudgetSeconds, 1, 600));

    public TimeSpan SiteCacheDuration => TimeSpan.FromMinutes(Math.Clamp(SiteCacheMinutes, 0, 1440));

    /// <summary>Returns one message per missing or unusable setting; empty when the integration can run.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(ApiKey)}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add(
                $"'{ConfigurationSectionName}:{nameof(Subdomain)}' is not configured " +
                $"(and no '{ConfigurationSectionName}:{nameof(BaseUrl)}' override was supplied).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(ProductFamilyHandle)}' is not configured.");
        }

        if (!MaxioCollectionMethods.TryResolve(PaymentCollectionMethod, out _))
        {
            problems.Add(
                $"'{ConfigurationSectionName}:{nameof(PaymentCollectionMethod)}' must be one of " +
                $"{string.Join(", ", MaxioCollectionMethods.SupportedValues)}.");
        }

        return problems;
    }
}

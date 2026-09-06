using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binding target for the <c>Maxio:</c> configuration section. Values are supplied by configuration
/// (user-secrets in development, environment variables or a vault elsewhere) and are never committed.
/// </summary>
public class MaxioSettings
{
    /// <summary>Name of the configuration section these settings bind from.</summary>
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username with the literal password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the OpenAPI specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Advanced Billing site, used to fill the <c>{site}</c> server variable.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim; otherwise the
    /// address is derived from <see cref="Subdomain"/> using the specification's server template.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method applied to subscriptions this application creates. Defaults to
    /// <c>remittance</c> (invoice billing) so shoppers can subscribe without a stored payment
    /// profile; set to <c>automatic</c> on a site that captures cards up front.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Prefix applied to every customer/subscription reference this application writes.</summary>
    public string ReferencePrefix { get; set; } = "eshop";

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries attempted for transient failures (429/5xx/network), on top of the first try.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalogue and site metadata are cached, in seconds. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Upper bound on how many reference slots are probed when a shopper re-subscribes to a plan they
    /// previously held. Each ended subscription permanently consumes one reference.
    /// </summary>
    public int MaxReferenceAttempts { get; set; } = 20;

    /// <summary>
    /// Server template taken from the <c>servers</c> block of the specification:
    /// <c>https://{site}.chargify.com</c>.
    /// </summary>
    private const string ServerUrlTemplate = "https://{site}.chargify.com";

    /// <summary>Returns the problems that prevent these settings from being used, or an empty list.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(ApiKey)}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(Subdomain)}' is missing (or supply '{ConfigurationSectionName}:{nameof(BaseUrl)}' instead).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(ProductFamilyHandle)}' is missing.");
        }

        if (TimeoutSeconds <= 0)
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(MaxRetryAttempts)}' must not be negative.");
        }

        if (MaxReferenceAttempts < 1)
        {
            problems.Add($"'{ConfigurationSectionName}:{nameof(MaxReferenceAttempts)}' must be at least one.");
        }

        return problems;
    }

    public bool IsConfigured => Validate().Count == 0;

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when set, otherwise the
    /// specification's server template with <c>{site}</c> replaced by <see cref="Subdomain"/>.
    /// A trailing slash is guaranteed so relative request paths resolve against the full base path.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : ServerUrlTemplate.Replace("{site}", Uri.EscapeDataString(Subdomain!.Trim()), StringComparison.Ordinal);

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}

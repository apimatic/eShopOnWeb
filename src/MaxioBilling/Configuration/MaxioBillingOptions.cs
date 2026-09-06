namespace Microsoft.eShopWeb.MaxioBilling.Configuration;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section.
/// Values are supplied through configuration only (user-secrets, environment variables,
/// key vault, ...) and are never committed to the repository.
/// </summary>
public class MaxioBillingOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the HTTP Basic <c>username</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. <c>my-site</c>. Substituted into the SDK base-URL template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional verbatim base-address override. When set it is used exactly as given and
    /// <see cref="Subdomain"/> is not substituted into it.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio hosting region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional plan handle used when a subscribe request does not name one.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio should collect payment for subscriptions this application creates.
    /// <para>
    /// eShopOnWeb never captures a card, so leaving Maxio on a site default of <c>automatic</c>
    /// makes it attempt to charge the full balance at signup and reject the subscription. The
    /// default here, <see cref="CollectionMethodAuto"/>, therefore picks the invoice-style method
    /// that matches the site's billing architecture: <c>remittance</c> on Relationship Invoicing,
    /// <c>invoice</c> on legacy Statements. The two are not interchangeable — each is rejected on
    /// the other architecture — which is why this is derived or configured, never hardcoded.
    /// </para>
    /// <para>
    /// Accepted values: <c>auto</c> (default), <c>site-default</c> (send nothing and let Maxio's
    /// site default apply), or one of <c>automatic</c>, <c>remittance</c>, <c>prepaid</c>,
    /// <c>invoice</c>.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Derive the collection method from the site's billing architecture.</summary>
    public const string CollectionMethodAuto = "auto";

    /// <summary>Send no collection method and let the Maxio site default apply.</summary>
    public const string CollectionMethodSiteDefault = "site-default";

    /// <summary>The collection-method values Maxio accepts, plus this integration's two directives.</summary>
    public static readonly IReadOnlyList<string> AllowedPaymentCollectionMethods =
    [
        CollectionMethodAuto, CollectionMethodSiteDefault,
        "automatic", "remittance", "prepaid", "invoice"
    ];

    /// <summary>Per-attempt HTTP timeout, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Budget for a whole logical call including retries and backoff, in seconds.</summary>
    public int CallBudgetSeconds { get; set; } = 30;

    /// <summary>Retry attempts after the first. The SDK floor is 1.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>How long the resolved product-family id and site currency are cached, in seconds.</summary>
    public int CatalogCacheSeconds { get; set; } = 300;

    /// <summary>True when enough is configured to talk to Maxio at all.</summary>
    public bool IsConfigured => ConfigurationProblems().Count == 0;

    /// <summary>Human-readable list of what is missing; empty when the options are usable.</summary>
    public IReadOnlyList<string> ConfigurationProblems()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{SectionName}:{nameof(ApiKey)}' is not set.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add($"Either '{SectionName}:{nameof(Subdomain)}' or '{SectionName}:{nameof(BaseUrl)}' must be set.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is not set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add($"'{SectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod) &&
            !AllowedPaymentCollectionMethods.Contains(PaymentCollectionMethod.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            problems.Add(
                $"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: " +
                string.Join(", ", AllowedPaymentCollectionMethods) + ".");
        }

        if (!string.IsNullOrWhiteSpace(Environment) &&
            !string.Equals(Environment, "US", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'{SectionName}:{nameof(Environment)}' must be 'US' or 'EU'.");
        }

        return problems;
    }

    /// <summary>True when the EU hosting region was selected.</summary>
    public bool UseEuRegion => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);
}

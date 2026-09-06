using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Everything the Maxio Advanced Billing integration reads from configuration. Bound from the
/// <c>Maxio</c> section; the credential values live in user-secrets (or the host's secret store),
/// never in a file under source control.
/// </summary>
public class MaxioSettings
{
    /// <summary>Name of the configuration section these settings are bound from.</summary>
    public const string ConfigurationSection = "Maxio";

    /// <summary>
    /// Maxio authenticates with HTTP Basic where the API key is the username and the password is an
    /// ignored placeholder. This is the placeholder the API expects - it is not a secret.
    /// </summary>
    public const string ApiKeyPasswordPlaceholder = "x";

    /// <summary><c>Maxio:ApiKey</c> - the site API key. Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> - the Maxio site subdomain the base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> - optional. When set it is used verbatim as the API base address and the
    /// subdomain is not consulted; use it for a mock server, a proxy, or a non-standard host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// <c>Maxio:ProductFamilyHandle</c> - handle of the product family whose products are the plans on
    /// offer. A handle, never a numeric id: Maxio reassigns numeric ids when a site is re-seeded.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:DefaultPlanHandle</c> - optional plan handle used when a subscribe request does not name
    /// one. Left unset, a request without a plan handle is rejected with the list of valid handles.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary><c>Maxio:Environment</c> - <c>US</c> (default) or <c>EU</c>; selects the Maxio server group.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> - optional; one of <c>remittance</c>, <c>invoice</c>,
    /// <c>automatic</c> or <c>prepaid</c>.
    /// <para>
    /// It decides whether Maxio tries to settle the first period's balance when the subscription is
    /// created. A product's "credit card not required" setting does <b>not</b> govern that; leaving the
    /// method unset means <c>automatic</c>, and a card-free signup is then rejected with "no payment method
    /// was on file". Left unconfigured this integration bills by remittance, which is what lets a shopper
    /// subscribe without capturing a card. Set it to <c>automatic</c> on a deployment that does capture one.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-attempt timeout applied by the SDK retry pipeline.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 15;

    /// <summary>Backstop timeout on the underlying <see cref="System.Net.Http.HttpClient"/>; also per attempt.</summary>
    public int HttpClientTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Total budget for one provider call including retries and backoff. This is the only bound the caller
    /// actually experiences - the two above each bound a single attempt.
    /// </summary>
    public int CallBudgetSeconds { get; set; } = 45;

    /// <summary>
    /// Extra attempts the SDK retry pipeline may make. The pipeline rejects 0, so 1 is the floor. Keep it
    /// low: a transport failure is retried even on writes, and only the write guard stops the duplicate.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Page size used when walking paginated provider list endpoints.</summary>
    public int PageSize { get; set; } = 100;

    public bool IsEuropeanSite =>
        !string.IsNullOrWhiteSpace(Environment) &&
        Environment!.Trim().StartsWith("EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the first configuration problem that would stop this integration from working, or null.
    /// Checked per request rather than at startup so that a deployment without billing configured still
    /// serves the rest of the API.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return ConfigurationSection + ":" + nameof(ApiKey) + " is not configured.";

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            return "Neither " + ConfigurationSection + ":" + nameof(Subdomain) + " nor " +
                   ConfigurationSection + ":" + nameof(BaseUrl) + " is configured.";

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            return ConfigurationSection + ":" + nameof(ProductFamilyHandle) + " is not configured.";

        return null;
    }
}

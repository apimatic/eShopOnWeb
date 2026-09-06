using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Everything needed to talk to a Maxio Advanced Billing site, bound from the "Maxio"
/// configuration section. No value here is ever hard-coded: the same build has to run against a
/// different site and a different catalog.
/// </summary>
public class MaxioSettings : ISubscriptionOptions
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>
    /// Site API key. Sent as the HTTP Basic username; the password is a literal "X".
    /// Supplied out of band - user secrets in development, the platform's secret store elsewhere.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain, used to derive the API host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, which is how a
    /// deployment points at a non-default host without changing anything else.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Plan handle used when a subscribe request does not name one.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio should collect payment for new subscriptions. Defaults to "remittance" so a
    /// shopper can subscribe without card capture; set "automatic" where a stored card is expected.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>How long a single Maxio call may take. Maxio itself cuts requests off at 120s.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Extra attempts made after a throttled or transient failure.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalog is cached. Plans change rarely and the API is concurrency limited.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>The API base address, derived from <see cref="Subdomain"/> unless overridden.</summary>
    public Uri ResolveBaseAddress()
    {
        var address = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // A base address must end in a slash or Uri drops its last segment when combining.
        if (!address.EndsWith("/", StringComparison.Ordinal)) address += "/";

        return new Uri(address, UriKind.Absolute);
    }

    /// <summary>Returns one message per configuration problem, empty when the settings are usable.</summary>
    public IReadOnlyCollection<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
            problems.Add($"'{ConfigurationSection}:ApiKey' is required (set it from the MAXIO_API_KEY environment variable via user secrets).");

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            problems.Add($"'{ConfigurationSection}:Subdomain' is required unless '{ConfigurationSection}:BaseUrl' is set.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            problems.Add($"'{ConfigurationSection}:ProductFamilyHandle' is required - it selects which products are offered as plans.");

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
            problems.Add($"'{ConfigurationSection}:BaseUrl' is not an absolute URL.");

        if (TimeoutSeconds <= 0)
            problems.Add($"'{ConfigurationSection}:TimeoutSeconds' must be greater than zero.");

        if (MaxRetryAttempts < 0)
            problems.Add($"'{ConfigurationSection}:MaxRetryAttempts' cannot be negative.");

        return problems;
    }
}

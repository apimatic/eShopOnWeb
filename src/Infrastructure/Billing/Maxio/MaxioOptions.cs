using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here is hard-coded: the same build runs against any Maxio site and catalogue.
/// </summary>
/// <remarks>
/// The credential-bearing keys (<see cref="ApiKey"/>, <see cref="Subdomain"/>,
/// <see cref="ProductFamilyHandle"/>) are supplied out-of-band — .NET user-secrets in development,
/// environment variables or a secret store elsewhere — and never live in the repository.
/// </remarks>
public class MaxioOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>US-hosted Advanced Billing sites. Base address template.</summary>
    private const string UsBaseUrlTemplate = "https://{0}.chargify.com";

    /// <summary>EU-hosted Advanced Billing sites. Base address template.</summary>
    private const string EuBaseUrlTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// Site API key. Sent as the HTTP Basic username, with the literal password <c>x</c>, per
    /// https://developers.maxio.com/http/getting-started/authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Advanced Billing site subdomain (the <c>{site}</c> in <c>https://{site}.chargify.com</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim (bar a trailing
    /// slash) instead of deriving an address from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hosting region of the Advanced Billing site: <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// How Maxio collects payment for subscriptions this application creates.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>remittance</c> so a shopper can subscribe without a stored payment method:
    /// Maxio invoices the subscription instead of trying to charge a card at signup. Sites on the
    /// legacy Statements architecture use <c>invoice</c> for the same effect; <c>automatic</c>
    /// charges the customer's stored payment method and requires one to exist.
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Prefix for the Maxio customer and subscription references this application owns. It keeps
    /// eShopOnWeb records identifiable on a site shared with other systems.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a throttled or transient request is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalogue and site metadata are cached, in seconds. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>Collection methods Maxio accepts, across both billing architectures.</summary>
    private static readonly string[] SupportedCollectionMethods = { "automatic", "remittance", "invoice", "prepaid" };

    /// <summary>Regions this client knows how to derive a base address for.</summary>
    private static readonly string[] SupportedEnvironments = { "US", "EU" };

    /// <summary>
    /// The API base address, without a trailing slash. Request paths are appended to it.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The options are incomplete or invalid.</exception>
    public string ResolveBaseAddress()
    {
        Validate();

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.Trim().TrimEnd('/');
        }

        var template = IsEuropeanEnvironment ? EuBaseUrlTemplate : UsBaseUrlTemplate;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain!.Trim());
    }

    private bool IsEuropeanEnvironment =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Throws when the integration cannot run with these settings, naming the keys at fault so the
    /// operator does not have to guess.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The options are incomplete or invalid.</exception>
    public void Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add($"'{SectionName}:{nameof(ApiKey)}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            problems.Add($"'{SectionName}:{nameof(Subdomain)}' is missing (or set '{SectionName}:{nameof(BaseUrl)}' to override the API base address).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !Uri.TryCreate(BaseUrl!.Trim().TrimEnd('/'), UriKind.Absolute, out _))
        {
            problems.Add($"'{SectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl)
            && !SupportedEnvironments.Contains(Environment?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"'{SectionName}:{nameof(Environment)}' must be one of {string.Join(", ", SupportedEnvironments)}.");
        }

        if (!IsSupportedCollectionMethod(PaymentCollectionMethod))
        {
            problems.Add($"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of {string.Join(", ", SupportedCollectionMethods)}.");
        }

        if (problems.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio subscription billing is not configured: " + string.Join(" ", problems));
        }
    }

    /// <summary>True when <paramref name="collectionMethod"/> is a value Maxio accepts.</summary>
    public static bool IsSupportedCollectionMethod(string? collectionMethod) =>
        !string.IsNullOrWhiteSpace(collectionMethod)
        && SupportedCollectionMethods.Contains(collectionMethod!.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The collection methods Maxio accepts.</summary>
    public static IReadOnlyList<string> AllowedCollectionMethods => SupportedCollectionMethods;
}

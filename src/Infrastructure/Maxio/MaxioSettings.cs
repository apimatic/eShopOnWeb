using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied through configuration providers only (user-secrets in development,
/// environment variables or a vault elsewhere) - none of them are ever hard-coded or committed.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username with the literal password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the spec.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Maxio site. Used to fill the <c>site</c> server variable of the spec's
    /// server template <c>https://{site}.chargify.com</c>.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscription plans eShopOnWeb offers.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional API base address override. When set it is used verbatim, instead of deriving the
    /// address from <see cref="Subdomain"/>. Useful for EU-hosted sites (whose server template is
    /// <c>https://{site}.ebilling.maxio.com</c>) and for pointing tests at a local stub.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method used when creating subscriptions, one of the values of the spec's
    /// <c>Collection-Method</c> schema. Defaults to <c>remittance</c> (invoice billing) so that
    /// plans which do not require a payment method can be subscribed to without card capture;
    /// set it to <c>automatic</c> on sites where a stored payment method is collected first.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a throttled or transient failure is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>How long the plan catalog is cached for. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Prefix applied to every customer and subscription reference eShopOnWeb writes into Maxio,
    /// so its records are distinguishable on a shared site.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>The default plan used when a subscribe request does not name one.</summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>True when the settings required to call Maxio are present and well formed.</summary>
    public bool IsConfigured => Validate().Count == 0;

    /// <summary>
    /// Returns a human readable description of every configuration problem, empty when valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{ConfigurationSectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                errors.Add($"'{ConfigurationSectionName}:{nameof(Subdomain)}' is required unless '{ConfigurationSectionName}:{nameof(BaseUrl)}' is set.");
            }
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"'{ConfigurationSectionName}:{nameof(BaseUrl)}' must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{ConfigurationSectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add($"'{ConfigurationSectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            errors.Add($"'{ConfigurationSectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        return errors;
    }

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise the
    /// spec's server template filled in with <see cref="Subdomain"/>. The result never has a
    /// trailing slash, so callers append spec paths such as <c>/customers.json</c> directly.
    /// </summary>
    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Cannot resolve the Maxio base address: neither '{ConfigurationSectionName}:{nameof(BaseUrl)}' nor '{ConfigurationSectionName}:{nameof(Subdomain)}' is set.");
        }

        return $"https://{Subdomain.Trim().Trim('/')}.chargify.com";
    }
}

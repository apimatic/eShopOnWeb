using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// The API key is a secret: supply it through user-secrets, environment variables or a vault.
/// It must never be committed to source control.
/// </remarks>
public sealed class MaxioOptions
{
    /// <summary>Name of the configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key. Sent as the HTTP Basic user name with the password <c>x</c>,
    /// per the <c>BasicAuth</c> security scheme in the OpenAPI specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The subdomain of the Advanced Billing site, used to template the server URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim and
    /// <see cref="Subdomain"/>/<see cref="Environment"/> are not consulted.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Hosting environment of the Advanced Billing account, as declared by the specification's
    /// <c>x-server-configuration</c>: <c>US</c> (default) or <c>EU</c>.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.Us;

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of retries for a retryable response. Zero disables retrying.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the site metadata (currency) read is cached, in minutes.</summary>
    public int SiteCacheMinutes { get; set; } = 60;

    /// <summary>Prefix applied to every customer/subscription reference this application creates.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>
    /// Collection method sent on new subscriptions, from the specification schema
    /// <c>Collection-Method</c>. Defaults to <c>remittance</c> (invoice the customer), because this
    /// flow enrolls shoppers without capturing a payment method; a site that captures cards up
    /// front should set <c>automatic</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = MaxioCollectionMethods.Remittance;

    /// <summary>Returns the configuration problems that prevent the integration from being used.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                errors.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
            }
            else if (!MaxioEnvironments.IsSupported(Environment))
            {
                errors.Add($"'{SectionName}:{nameof(Environment)}' must be one of {MaxioEnvironments.Supported}.");
            }
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute http(s) URL.");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            errors.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        if (!MaxioCollectionMethods.IsSupported(PaymentCollectionMethod))
        {
            errors.Add($"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of {MaxioCollectionMethods.Supported}.");
        }

        return errors;
    }

    public bool IsConfigured => Validate().Count == 0;

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise the
    /// environment's server template from the specification with the subdomain substituted in.
    /// </summary>
    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        var template = MaxioEnvironments.ServerTemplateFor(Environment);
        return template.Replace("{site}", Subdomain?.Trim(), StringComparison.Ordinal);
    }
}

/// <summary>
/// The Advanced Billing hosting environments and their production server templates, taken from the
/// <c>x-server-configuration</c> block of the Maxio OpenAPI specification.
/// </summary>
public static class MaxioEnvironments
{
    public const string Us = "US";
    public const string Eu = "EU";

    private static readonly Dictionary<string, string> ServerTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        [Us] = "https://{site}.chargify.com",
        [Eu] = "https://{site}.ebilling.maxio.com"
    };

    public static string Supported => string.Join(", ", ServerTemplates.Keys.OrderBy(k => k, StringComparer.Ordinal));

    public static bool IsSupported(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) && ServerTemplates.ContainsKey(environment.Trim());

    public static string ServerTemplateFor(string? environment)
    {
        if (!string.IsNullOrWhiteSpace(environment) && ServerTemplates.TryGetValue(environment.Trim(), out var template))
        {
            return template;
        }

        throw new ArgumentOutOfRangeException(
            nameof(environment), environment, $"Unknown Maxio environment. Supported values: {Supported}.");
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here has a hard-coded value: the same build has to run against any Maxio site
/// and any catalog. Supply them through user-secrets, environment variables (<c>Maxio__ApiKey</c>,
/// <c>Maxio__Subdomain</c>, ...) or any other configuration provider.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic user name with the fixed password "x", per the
    /// <c>BasicAuth</c> security scheme in the OpenAPI specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain used to template the server URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim instead of deriving the
    /// server URL from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, from the specification's server configuration: "US" (default) maps
    /// to <c>https://{site}.chargify.com</c>, "EU" to <c>https://{site}.ebilling.maxio.com</c>.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure (network error, 429, 5xx) is retried.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential, jittered retry backoff, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 200;

    /// <summary>
    /// Payment collection method used when creating subscriptions. The demo plans do not require a
    /// payment method, so invoicing ("remittance") is used rather than automatic card collection.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Namespace prefix for the customer/subscription references eShopOnWeb writes into Maxio, so the
    /// records this app owns are recognisable on a shared site.
    /// </summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    public const string UsEnvironment = "US";
    public const string EuEnvironment = "EU";

    /// <summary>
    /// Resolves the API base address: the verbatim <see cref="BaseUrl"/> when supplied, otherwise the
    /// environment's server template with <see cref="Subdomain"/> substituted for <c>{site}</c>.
    /// A trailing slash is guaranteed so relative request paths resolve against the full base.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Maxio is not configured: {string.Join(" ", errors)}");
        }

        var address = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : string.Equals(Environment, EuEnvironment, StringComparison.OrdinalIgnoreCase)
                ? $"https://{Subdomain!.Trim()}.ebilling.maxio.com"
                : $"https://{Subdomain!.Trim()}.chargify.com";

        return new Uri(address.TrimEnd('/') + "/", UriKind.Absolute);
    }

    /// <summary>Returns a human readable problem per missing or invalid setting; empty when usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            errors.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            errors.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        return errors;
    }

    public bool IsConfigured => Validate().Count == 0;
}

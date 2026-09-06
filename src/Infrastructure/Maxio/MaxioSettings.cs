using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// Values are supplied through configuration only (user-secrets, environment variables, key vault, ...).
/// Nothing here may be committed to source control.
/// </remarks>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Used as the user name of the HTTP Basic credential defined by the OpenAPI
    /// <c>BasicAuth</c> security scheme (the password is the literal <c>x</c>).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Maxio site. Substituted into the <c>{site}</c> server template declared by the
    /// specification. Ignored when <see cref="BaseUrl"/> is supplied.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family whose products are published as subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim and no URL is derived
    /// from <see cref="Subdomain"/>/<see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, matching the environments declared by the specification's
    /// <c>x-server-configuration</c>: <c>US</c> (default) or <c>EU</c>. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay used for the exponential retry back-off, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the plan catalogue and site metadata are cached, in seconds. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>US environment server template declared by the specification.</summary>
    internal const string UsServerTemplate = "https://{site}.chargify.com";

    /// <summary>EU environment server template declared by the specification.</summary>
    internal const string EuServerTemplate = "https://{site}.ebilling.maxio.com";

    internal const string UsEnvironment = "US";
    internal const string EuEnvironment = "EU";

    /// <summary>
    /// Resolves the API base address: the <see cref="BaseUrl"/> override when present, otherwise the
    /// specification's server template for the configured environment with <c>{site}</c> replaced by
    /// <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var overrideUri))
            {
                throw new InvalidOperationException($"Maxio:BaseUrl '{BaseUrl}' is not an absolute URI.");
            }

            return EnsureTrailingSlash(overrideUri);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not supplied.");
        }

        var template = ResolveServerTemplate();
        return EnsureTrailingSlash(new Uri(template.Replace("{site}", Subdomain.Trim(), StringComparison.Ordinal), UriKind.Absolute));
    }

    private string ResolveServerTemplate()
    {
        var environment = string.IsNullOrWhiteSpace(Environment) ? UsEnvironment : Environment.Trim();

        if (string.Equals(environment, UsEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return UsServerTemplate;
        }

        if (string.Equals(environment, EuEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return EuServerTemplate;
        }

        throw new InvalidOperationException(
            $"Maxio:Environment '{Environment}' is not supported. Use '{UsEnvironment}' or '{EuEnvironment}', or set Maxio:BaseUrl explicitly.");
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}

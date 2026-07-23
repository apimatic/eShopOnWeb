using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Only <see cref="ApiKey"/> is sensitive and it must come
/// from .NET user-secrets or an environment variable — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    private const string UsHostFormat = "https://{0}.chargify.com";
    private const string EuHostFormat = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Sent as the HTTP Basic username. Never commit this.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain that identifies the tenant, e.g. <c>your-site</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region (<c>US</c> or <c>EU</c>). This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit override for the outbound base URL. When set it wins over the subdomain-derived
    /// host, so the same build can be pointed at production, a dev/sandbox tenant, or a local
    /// mock server purely through configuration.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>The primary plan customers subscribe to.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>The alternate plan, used as the upgrade/downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>The metered component that pay-as-you-go usage is reported against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is honoured verbatim,
    /// and only when it is absent is the host derived from <see cref="Subdomain"/> and
    /// <see cref="Environment"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither an explicit base URL nor a subdomain was configured, or the explicit base URL is
    /// not a well-formed absolute URI.
    /// </exception>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var explicitUri))
            {
                throw new InvalidOperationException(
                    $"'{MaxioSettings.CONFIG_SECTION}:{nameof(BaseUrl)}' is not a valid absolute URL: '{BaseUrl}'.");
            }

            return EnsureTrailingSlash(explicitUri);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Configure either '{CONFIG_SECTION}:{nameof(BaseUrl)}' or '{CONFIG_SECTION}:{nameof(Subdomain)}' " +
                "so the billing integration knows which server to target.");
        }

        var hostFormat = IsEuropeanRegion() ? EuHostFormat : UsHostFormat;

        return EnsureTrailingSlash(new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            hostFormat, Subdomain.Trim())));
    }

    private bool IsEuropeanRegion() =>
        string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Guarantees the base address ends in a slash so relative request paths append to it rather
    /// than replacing its last segment — which keeps sub-path targets (such as a mock server
    /// mounted under a prefix) working.
    /// </summary>
    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");
}

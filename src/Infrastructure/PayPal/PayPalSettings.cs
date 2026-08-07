using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> configuration section.
/// Values are supplied via configuration / user-secrets and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>The PayPal environment, e.g. "sandbox".</summary>
    public string? Environment { get; set; }

    /// <summary>Optional explicit base URL. When set, it is used verbatim instead of deriving one.</summary>
    public string? BaseUrl { get; set; }

    // Base URLs are defined by the PayPal OpenAPI specs' `servers` block.
    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        // Resolving the base URL also validates the Environment when no explicit BaseUrl is given.
        _ = ResolveBaseUrl();
    }

    /// <summary>
    /// Resolves the API base address. An explicit <see cref="BaseUrl"/> wins and is used verbatim;
    /// otherwise the URL is derived from <see cref="Environment"/> per the specs' server definitions.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => SandboxBaseUrl,
            "live" or "production" => LiveBaseUrl,
            "" => throw new InvalidOperationException("PayPal:Environment is not configured (expected 'sandbox')."),
            var other => throw new InvalidOperationException($"Unsupported PayPal:Environment '{other}'. Expected 'sandbox'.")
        };
    }
}

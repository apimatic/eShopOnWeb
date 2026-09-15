using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied by
/// configuration / user-secrets (never hard-coded), so the same build can run against a different
/// PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production". Selects the API base URL when BaseUrl is unset.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code all amounts are expressed in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base for EVERY PayPal call
    /// (including the OAuth token request), instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The sandbox host, per the PayPal OpenAPI specs' <c>servers</c> block.</summary>
    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    /// <summary>The live host (PayPal's documented production counterpart; not listed in the sandbox-only specs).</summary>
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolves the effective API base URL: the verbatim override, else derived from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = (Environment ?? string.Empty).Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is not configured. Set PAYPAL_CLIENT_ID via user-secrets.");
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is not configured. Set PAYPAL_CLIENT_SECRET via user-secrets.");
        }
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured.");
        }
    }
}

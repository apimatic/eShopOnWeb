using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings bound from the <c>PayPal:</c> configuration section. None of the
/// values are hard-coded; they come from configuration (user-secrets / environment) so the same
/// build can run against a different PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST client id (<c>PayPal:ClientId</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST client secret (<c>PayPal:ClientSecret</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment, "sandbox" or "live"/"production" (<c>PayPal:Environment</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency payments are taken in (<c>PayPal:Currency</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL (<c>PayPal:BaseUrl</c>). When set it is used verbatim for
    /// every PayPal call, including the OAuth token request; otherwise the base URL is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The base URL every PayPal request (token and API) is made against.</summary>
    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl!.TrimEnd('/');
            }

            var isLive = Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                || Environment.Equals("production", StringComparison.OrdinalIgnoreCase);
            return isLive ? LiveBaseUrl : SandboxBaseUrl;
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured.");
    }
}

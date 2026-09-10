using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. No values are hard-coded;
/// the same build runs against a different PayPal account by changing configuration alone.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>. Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call
    /// (including the OAuth token request); otherwise the base is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address, honouring the <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }
}

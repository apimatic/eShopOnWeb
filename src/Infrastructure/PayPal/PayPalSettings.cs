using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST settings, bound from the <c>PayPal:</c> configuration section. None of these values are
/// hard-coded anywhere; the same build runs against a different PayPal account purely by changing config.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>/<c>production</c>. Selects the default API base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The settlement currency (ISO 4217 code), e.g. USD.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim for every PayPal call — including the
    /// token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive =>
        Environment is not null &&
        (Environment.Equals("live", StringComparison.OrdinalIgnoreCase) ||
         Environment.Equals("production", StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves the API base address, honouring an explicit <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return IsLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}

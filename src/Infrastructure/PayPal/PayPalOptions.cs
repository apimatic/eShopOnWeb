using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. The
/// values themselves are supplied through user-secrets / environment and never live in the
/// repository.
/// </summary>
public class PayPalOptions : IPaymentSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Selects the default API base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The ISO-4217 currency all amounts are charged in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional explicit API base URL. When set, it is used verbatim for every PayPal
    /// call — including the OAuth token request — instead of one derived from
    /// <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    public string CurrencyCode => Currency;

    /// <summary>Resolves the API base address per the spec's server templating: an explicit
    /// <see cref="BaseUrl"/> wins; otherwise it is derived from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl!.TrimEnd('/');

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com"
        };
    }
}

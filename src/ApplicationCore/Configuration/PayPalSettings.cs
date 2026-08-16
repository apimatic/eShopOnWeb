namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via .NET
/// user-secrets / environment and are never hard-coded, so the same build runs against any PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production". Selects the default API base address.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for all amounts (e.g. USD).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim for every PayPal call — including the
    /// OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolves the effective API base address: BaseUrl override, else per-environment default.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }

    public string ResolveCurrency() => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();
}

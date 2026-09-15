using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal settings bound from the <c>PayPal:</c> configuration section. The values are never
/// hard-coded and never committed to the repository — they are supplied via user-secrets /
/// environment variables. The same build runs against a different PayPal account by changing
/// only configuration.
/// </summary>
public class PayPalOptions : IPaymentSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production". Ignored when <see cref="BaseUrl"/> is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency used for every order (from <c>PayPal:Currency</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every PayPal
    /// call — including the credential/token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolve the base address: the explicit override wins, otherwise derive from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl,
        };
    }

    // Currencies PayPal does not accept decimal places for, and those that use three.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "HUF", "JPY", "TWD" };
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "BHD", "KWD", "OMR", "TND" };

    /// <summary>Format a decimal as a PayPal money <c>value</c> string with the currency's decimal places.</summary>
    public static string FormatMoney(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0
            : ThreeDecimalCurrencies.Contains(currency) ? 3
            : 2;
        return amount.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Parse a PayPal money <c>value</c> string into a decimal.</summary>
    public static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}

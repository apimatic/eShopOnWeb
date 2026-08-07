using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// PayPal configuration, bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment and are never hard-coded, so the same build can run against a
/// different PayPal app.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Always "sandbox" for this integration.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional explicit API base address. When set it is used verbatim.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the REST API base address: the explicit <see cref="BaseUrl"/> when provided,
    /// otherwise the well-known host for the configured environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var environment = (Environment ?? "sandbox").Trim().ToLowerInvariant();
        return environment switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new InvalidOperationException(
                $"Unsupported PayPal environment '{Environment}'. Set PayPal:Environment to 'sandbox' or provide PayPal:BaseUrl.")
        };
    }
}

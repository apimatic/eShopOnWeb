using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section. Values are
/// supplied via configuration / user-secrets / environment variables and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Target environment. Only "sandbox" is supported for this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base address is
    /// derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>Resolves the API base address, honouring an explicit <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    /// <summary>Throws if the settings required to talk to PayPal are missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables via user-secrets).");
        }
    }
}

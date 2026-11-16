using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Settings bound from the <c>Finix</c> configuration section. No credential value
/// is ever written into the repository; the values come from user-secrets or the
/// process environment at runtime.
/// </summary>
public sealed class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string MerchantId { get; set; } = string.Empty;

    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every
    /// Finix call; otherwise the address is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://finix.live-payments-api.com"
            : "https://finix.sandbox-payments-api.com";
    }
}

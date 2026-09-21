using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via
/// .NET user-secrets / environment (never committed). See <see cref="Validate"/> for fail-fast.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST client secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment name (from <c>PAYPAL_ENVIRONMENT</c>); only "sandbox" is a known SDK environment.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all charges (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional base-URL override; when set it is used verbatim for every PayPal call including the token request.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Fail-fast validation. Every credential part is checked independently — a blank part is not a
    /// missing one — so the host refuses to start rather than discovering it as a 401 in production.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException(
                "PayPal:ClientId is not configured. Set it via user-secrets or environment before starting the app.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException(
                "PayPal:ClientSecret is not configured. Set it via user-secrets or environment before starting the app.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException(
                "PayPal:Currency is not configured. Set it via user-secrets or environment before starting the app.");
        if (string.IsNullOrWhiteSpace(Environment))
            throw new InvalidOperationException(
                "PayPal:Environment is not configured. Set it via user-secrets or environment before starting the app.");
    }
}

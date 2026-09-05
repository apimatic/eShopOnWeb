using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values come from the environment or user
/// secrets, never from a file in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SECTION_NAME = "PayPal";
    public const string CLIENT_ID_KEY = "ClientId";
    public const string CLIENT_SECRET_KEY = "ClientSecret";
    public const string ENVIRONMENT_KEY = "Environment";
    public const string CURRENCY_KEY = "Currency";
    public const string BASE_URL_KEY = "BaseUrl";

    /// <summary>REST app client id. Maps to <c>PAYPAL_CLIENT_ID</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app secret. Maps to <c>PAYPAL_CLIENT_SECRET</c>.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>sandbox</c> or <c>live</c>. Maps to <c>PAYPAL_ENVIRONMENT</c>.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO currency used for every money movement. Maps to <c>PAYPAL_CURRENCY</c>.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address for every call,
    /// including the token request, instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address without a trailing slash, so paths can be appended directly.
    /// </summary>
    public string BaseAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return CreateUri(BaseUrl, $"{SECTION_NAME}:{nameof(BaseUrl)}").ToString().TrimEnd('/');
            }

            var environment = (Environment ?? string.Empty).Trim().ToLowerInvariant();
            return environment switch
            {
                "sandbox" => CreateUri("https://api-m.sandbox.paypal.com", $"{SECTION_NAME}:{nameof(Environment)}").ToString().TrimEnd('/'),
                "live" or "production" => CreateUri("https://api-m.paypal.com", $"{SECTION_NAME}:{nameof(Environment)}").ToString().TrimEnd('/'),
                _ => throw new InvalidOperationException(
                    $"{SECTION_NAME}:{nameof(Environment)} must be either 'sandbox' or 'live', or {SECTION_NAME}:{nameof(BaseUrl)} must be set.")
            };
        }
    }

    /// <summary>
    /// True when everything needed to talk to the processor is present. Checked before any money
    /// moves, so the answer to a shopper is a clear 'payments are not configured' rather than a crash.
    /// </summary>
    public bool IsConfigured => Problem is null;

    /// <summary>What is missing, in terms whoever deploys this can act on.</summary>
    public string? Problem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ClientId))
            {
                return $"{SECTION_NAME}:{nameof(ClientId)} is not configured.";
            }

            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                return $"{SECTION_NAME}:{nameof(ClientSecret)} is not configured.";
            }

            if (string.IsNullOrWhiteSpace(Currency) || Currency.Trim().Length != 3)
            {
                return $"{SECTION_NAME}:{nameof(Currency)} must be a three-letter ISO currency code.";
            }

            try
            {
                var _ = BaseAddress;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }

            return null;
        }
    }
    private static Uri CreateUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value.TrimEnd('/'), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute http(s) URL.");
        }

        return uri;
    }
}

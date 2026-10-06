using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// An error surfaced by the Maxio Advanced Billing integration. The message is safe to
/// return to API callers; <see cref="StatusCode"/> is the HTTP status the caller should see.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Converts a provider error into a caller-safe exception that carries the provider's
    /// HTTP status: a provider 4xx surfaces as that same 4xx, anything else as 5xx.
    /// </summary>
    public static MaxioApiException FromProviderError(int statusCode, string? providerDetail)
    {
        var message = DefaultMessageFor(statusCode);
        if (!string.IsNullOrWhiteSpace(providerDetail))
        {
            var detail = providerDetail.Trim();
            if (detail.Length > 500)
            {
                detail = detail[..500];
            }
            message = $"{message} (billing provider: {detail})";
        }
        return new MaxioApiException(statusCode, message);
    }

    private static string DefaultMessageFor(int statusCode) => statusCode switch
    {
        401 or 403 => "The billing provider rejected the configured credentials.",
        404 => "The requested billing record was not found.",
        422 => "The billing provider rejected the request as invalid.",
        >= 400 and < 500 => "The billing provider rejected the request.",
        _ => "The billing provider returned an unexpected error."
    };
}
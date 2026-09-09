using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an error response. Extends the ApplicationCore billing
/// exception so API endpoints can translate it without depending on Infrastructure types.
/// </summary>
public class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(int statusCode, string message, string? responseBody = null, Exception? innerException = null)
        : base(message, statusCode, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public int StatusCode { get; }

    /// <summary>The raw response body, when available (useful for diagnostics/logging).</summary>
    public string? ResponseBody { get; }
}

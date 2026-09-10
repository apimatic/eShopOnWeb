using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an error response. Derives from
/// <see cref="SubscriptionBillingException"/> so the API layer can map billing failures to
/// HTTP 502 without referencing this provider-specific type.
/// </summary>
public class MaxioApiException : SubscriptionBillingException
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base($"Maxio API request failed ({(int)statusCode} {statusCode}): {message}")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

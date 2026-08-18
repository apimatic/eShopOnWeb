using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS provider integration presents at its boundary. Whatever went wrong at the
/// provider — a rejected request, an unreachable host, an unreadable response — is translated into this type
/// so callers have one thing to handle. Carries the provider's HTTP status (when there was one) and its
/// error code (when one could be read) so the caller can map the failure deliberately.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>The provider's HTTP status, when the provider answered. Null for a transport failure.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The provider's own error code, when one could be read from the error body.</summary>
    public int? ProviderErrorCode { get; }
}

using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the messaging provider seam raises, for both provider (non-2xx) errors and
/// transport failures. Carries the provider's HTTP status where one was returned (null for a transport
/// failure or an unreadable error) plus the provider's own error code/message where available, so the
/// integration boundary can map failures to caller-facing outcomes deliberately.
/// </summary>
public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, HttpStatusCode? statusCode = null, int? providerErrorCode = null, string? providerErrorMessage = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }

    /// <summary>The provider's HTTP status, if the provider answered; null for a transport failure.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The provider's own numeric error code, if present in the error body.</summary>
    public int? ProviderErrorCode { get; }

    /// <summary>The provider's own error message, if present in the error body.</summary>
    public string? ProviderErrorMessage { get; }
}

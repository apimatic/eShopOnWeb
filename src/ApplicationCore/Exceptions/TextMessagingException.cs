using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging-provider boundary: an API rejection (carrying the provider's
/// HTTP status and error code) or a transport failure (no status — nothing answered).
/// </summary>
public class TextMessagingException : Exception
{
    public TextMessagingException(string message, HttpStatusCode? statusCode = null, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public HttpStatusCode? StatusCode { get; }
    public int? ProviderErrorCode { get; }
}

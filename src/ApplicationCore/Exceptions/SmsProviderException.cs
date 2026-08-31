using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The one failure type leaving the messaging-provider boundary: API rejections
/// (carrying the provider's HTTP status), transport failures, and unreadable
/// provider responses (no status).
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status when it answered; null for transport/parse failures.</summary>
    public HttpStatusCode? StatusCode { get; }
}

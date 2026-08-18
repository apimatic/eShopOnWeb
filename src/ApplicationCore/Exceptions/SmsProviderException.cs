using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the messaging provider surfaces to the rest of the application. It carries
/// the provider's HTTP status (when there was one) so a caller-facing boundary can map it deliberately.
/// The message deliberately never carries the provider's response body, which can echo the destination
/// number.
/// </summary>
public class SmsProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

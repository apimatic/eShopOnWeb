using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type leaving the Maxio integration boundary. Carries the provider's
/// HTTP status where one exists so callers can distinguish a client error (4xx) from a
/// provider/transport failure (5xx). Message must always be caller-safe.
/// </summary>
public class MaxioBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

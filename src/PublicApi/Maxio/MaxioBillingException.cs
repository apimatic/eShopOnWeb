using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Error raised at the Maxio integration boundary. <see cref="StatusCode"/> is the HTTP status the
/// PublicApi host should answer with, and <see cref="Exception.Message"/> is always caller-safe.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioBillingException(HttpStatusCode statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

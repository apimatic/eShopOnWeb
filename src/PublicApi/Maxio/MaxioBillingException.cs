using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type leaving the Maxio integration boundary. Carries the HTTP status
/// the caller should see and a caller-safe message (provider bodies are logged, not leaked).
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

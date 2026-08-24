using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type leaving the Maxio integration boundary. Carries the HTTP
/// status the caller should see and a caller-safe message; provider detail stays in
/// the logs via the inner exception.
/// </summary>
public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type leaving the Maxio integration boundary. Carries the HTTP
/// status the caller should see: provider 4xx statuses are carried through, provider
/// 5xx / transport / unprocessable-response failures surface as 5xx.
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

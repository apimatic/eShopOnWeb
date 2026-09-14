using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Failure raised at the Maxio integration boundary. <see cref="Message"/> is always safe to
/// return to an API caller (never SDK/framework exception text), and <see cref="StatusCode"/> is
/// the HTTP status the endpoint should answer with.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

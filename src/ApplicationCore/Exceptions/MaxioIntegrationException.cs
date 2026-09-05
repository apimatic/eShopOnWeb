using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the Maxio integration boundary. Carries the HTTP status a caller should see -
/// a passed-through 4xx for a rejected request, or 502 for a provider/transport failure - so the
/// distinction between "you sent something invalid" and "the provider is unavailable" survives the
/// translation out of the Maxio SDK's own exception types.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioIntegrationException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

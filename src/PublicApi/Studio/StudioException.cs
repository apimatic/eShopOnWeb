using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Raised when the app cannot carry out a studio request. Translated by the ExceptionMiddleware into
/// an HTTP error status plus the reason.
/// </summary>
public class StudioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public StudioException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static StudioException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static StudioException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

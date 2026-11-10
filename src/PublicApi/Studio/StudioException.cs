using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Raised when the studio integration cannot carry out a request. The exception middleware turns it
/// into a response with <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class StudioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public StudioException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static StudioException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static StudioException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static StudioException Unprocessable(string message) => new(HttpStatusCode.UnprocessableEntity, message);
}

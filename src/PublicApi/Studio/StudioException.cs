using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Raised when the app cannot carry out a studio request. The status code and the reason are
/// returned to the caller by the exception middleware.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(400, message);
    public static StudioException NotFound(string message) => new(404, message);
    public static StudioException Conflict(string message) => new(409, message);
}

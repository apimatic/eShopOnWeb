using System;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// A request the app cannot carry out. Rendered by the exception middleware as an HTTP
/// status with a reason.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(400, message);
    public static StudioException NotFound(string message) => new(404, message);
    public static StudioException Conflict(string message) => new(409, message);
    public static StudioException Unprocessable(string message) => new(422, message);
}

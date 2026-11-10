using System;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// An error that is reported to the caller with a specific HTTP status and a reason.
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

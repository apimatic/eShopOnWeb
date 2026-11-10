using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Raised when the app cannot carry out a studio request. Carries the HTTP status and the reason
/// that is returned to the caller.
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

/// <summary>
/// The studio system accepted a request but its answer could not be read, so the outcome has to be
/// confirmed by looking at the studio's records.
/// </summary>
public sealed class MindbodyUnreadableResponseException : StudioException
{
    public MindbodyUnreadableResponseException(Exception inner)
        : base(502, "The studio system's answer could not be read.", inner)
    {
    }
}

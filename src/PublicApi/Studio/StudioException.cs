using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// The studio feature can't carry out a request. The status code and the message are handed to the caller.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    /// <summary>Seconds after which the caller may try again, when it makes sense to.</summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>Mindbody's own error code, when the failure came from Mindbody.</summary>
    public string? MindbodyCode { get; init; }

    /// <summary>
    /// Mindbody accepted the call (a 2xx answer) but its reply could not be read. The call most likely took effect,
    /// so callers that change something check what happened instead of reporting a failure.
    /// </summary>
    public bool ReplyUnreadable { get; init; }

    public StudioException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public StudioException(HttpStatusCode statusCode, string message, Exception? inner = null)
        : this((int)statusCode, message, inner)
    {
    }

    public static StudioException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static StudioException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static StudioException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

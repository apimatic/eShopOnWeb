using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// An error the studio feature can describe to the caller: carries the HTTP status and a reason.
/// </summary>
public class StudioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>The Mindbody error code behind this failure, when there is one.</summary>
    public string? Code { get; init; }

    public StudioException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static StudioException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static StudioException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

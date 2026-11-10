using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// A request the app cannot carry out. Carries the HTTP status (4xx/5xx) and the reason given to the caller.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string reason, Exception? inner = null) : base(reason, inner)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string reason) => new(400, reason);
    public static StudioException NotFound(string reason) => new(404, reason);
    public static StudioException Conflict(string reason) => new(409, reason);
}

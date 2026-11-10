using System;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// A request the studio integration cannot carry out; carries the HTTP status and the reason given to the caller.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string reason) : base(reason)
    {
        StatusCode = statusCode;
    }

    public static StudioException BadRequest(string reason) => new(400, reason);
    public static StudioException NotFound(string reason) => new(404, reason);
    public static StudioException Conflict(string reason) => new(409, reason);
}

/// <summary>
/// Mindbody answered, but the answer could not be read; what it did is unknown until checked.
/// </summary>
public class StudioResponseUnreadableException : StudioException
{
    public StudioResponseUnreadableException() : base(502, "The studio system returned an unexpected response.")
    {
    }
}

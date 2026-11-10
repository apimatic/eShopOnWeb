using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>An error that the API reports to its caller with a status code and a reason.</summary>
public class StudioException : Exception
{
    public StudioException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static StudioException BadRequest(string message) => new(400, message);
    public static StudioException NotFound(string message) => new(404, message);
    public static StudioException Conflict(string message) => new(409, message);
}

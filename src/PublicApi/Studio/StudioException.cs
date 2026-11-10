using System;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// A request the app cannot carry out. The status code and message are returned to the caller.
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
/// Mindbody answered with an error.
/// </summary>
public class MindbodyException : StudioException
{
    public string? Code { get; }

    public MindbodyException(int statusCode, string? code, string message) : base(statusCode, message)
    {
        Code = code;
    }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// A request the studio integration cannot carry out. Rendered by ExceptionMiddleware as a status code plus reason.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }
    public string? MindbodyCode { get; }

    public StudioException(int statusCode, string message, string? mindbodyCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        MindbodyCode = mindbodyCode;
    }
}

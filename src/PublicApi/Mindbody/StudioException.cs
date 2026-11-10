using System;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// The app cannot carry out a request; the status code and message are returned to the caller.
/// </summary>
public class StudioException : Exception
{
    public int StatusCode { get; }

    public StudioException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Mindbody answered with an error.
/// </summary>
public class MindbodyApiException : Exception
{
    public int HttpStatus { get; }
    public string? Code { get; }

    public MindbodyApiException(int httpStatus, string? code, string message) : base(message)
    {
        HttpStatus = httpStatus;
        Code = code;
    }
}

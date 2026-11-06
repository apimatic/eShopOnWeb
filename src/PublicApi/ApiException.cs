using System;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Thrown when the app cannot carry out a request; the middleware answers with <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

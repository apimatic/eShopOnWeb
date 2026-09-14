using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails. Carries the upstream HTTP
/// status code so callers (e.g. <see cref="Microsoft.eShopWeb.PublicApi.Middleware.ExceptionMiddleware"/>)
/// can map it to an appropriate response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

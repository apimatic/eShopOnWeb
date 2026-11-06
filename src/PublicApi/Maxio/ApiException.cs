using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// An error that is returned to the caller as an HTTP status with a reason.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

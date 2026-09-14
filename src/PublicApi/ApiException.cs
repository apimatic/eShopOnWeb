using System;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Carries an explicit HTTP status code and message to the API error middleware.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

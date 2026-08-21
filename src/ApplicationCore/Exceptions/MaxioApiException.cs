using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio Advanced Billing returns an unexpected or failed HTTP response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioApiException(string message, int statusCode, string? responseBody = null) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

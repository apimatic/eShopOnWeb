using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An error that should be returned to the API caller with a specific HTTP status code.
/// </summary>
public class ApiException : Exception
{
    public ApiException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}

public class FinixApiException : ApiException
{
    public FinixApiException(int statusCode, string message, string? code = null, string? rawResponse = null)
        : base(statusCode, message, code)
    {
        RawResponse = rawResponse;
    }

    public string? RawResponse { get; }
}

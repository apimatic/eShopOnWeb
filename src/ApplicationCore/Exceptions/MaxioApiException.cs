using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public int? StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioApiException(string message) : base(message)
    {
    }

    public MaxioApiException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public MaxioApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

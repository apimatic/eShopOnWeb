using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns a non-success status. Carries the status code and a
/// (truncated) response body to aid diagnosis. Credentials are never included.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, string? responseBody)
        : base($"Maxio API call '{operation}' failed with status {(int)statusCode} ({statusCode}). {responseBody}")
    {
        StatusCode = statusCode;
        Operation = operation;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Operation { get; }

    public string? ResponseBody { get; }
}

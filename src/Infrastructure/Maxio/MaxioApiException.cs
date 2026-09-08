using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an unexpected status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? responseBody, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public int StatusCodeValue => (int)StatusCode;

    public string? ResponseBody { get; }

    public bool IsUnprocessable => StatusCode == HttpStatusCode.UnprocessableEntity;

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    public bool IsClientError => StatusCodeValue >= 400 && StatusCodeValue < 500;
}

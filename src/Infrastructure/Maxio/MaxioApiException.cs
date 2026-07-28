using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio Advanced Billing API call returns an unexpected/non-success status.
/// Carries the HTTP status and (best-effort) response body for diagnostics and for mapping to
/// an appropriate API response upstream.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string requestDescription, string? responseBody, Exception? inner = null)
        : base($"Maxio API call failed ({(int)statusCode} {statusCode}) for {requestDescription}."
               + (string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $" Response: {responseBody}"), inner)
    {
        StatusCode = statusCode;
        RequestDescription = requestDescription;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestDescription { get; }

    public string? ResponseBody { get; }
}

using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Wraps a non-success response from the Maxio Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API request failed with status {(int)statusCode} ({statusCode}): {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    private static string Truncate(string body)
        => body.Length <= 500 ? body : body.Substring(0, 500) + "...";
}

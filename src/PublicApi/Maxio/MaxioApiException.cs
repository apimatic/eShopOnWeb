using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

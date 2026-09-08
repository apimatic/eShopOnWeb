using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Advanced Billing API responds with an error status code.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public bool IsUnprocessableEntity => StatusCode == HttpStatusCode.UnprocessableEntity;

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
}

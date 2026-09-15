using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string message, HttpStatusCode statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
}

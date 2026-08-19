using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
}

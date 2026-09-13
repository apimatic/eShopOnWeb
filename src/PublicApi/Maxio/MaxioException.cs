using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public MaxioException(HttpStatusCode statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

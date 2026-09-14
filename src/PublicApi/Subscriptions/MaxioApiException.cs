using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio Advanced Billing returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}

public sealed class MaxioDuplicateRequestException : MaxioApiException
{
    public MaxioDuplicateRequestException(string responseBody)
        : base(HttpStatusCode.Conflict, responseBody)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio returned HTTP {statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string ResponseBody { get; }
}

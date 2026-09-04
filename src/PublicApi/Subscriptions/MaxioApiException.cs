using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string? responseBody)
        : base($"Maxio API returned HTTP {statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
}

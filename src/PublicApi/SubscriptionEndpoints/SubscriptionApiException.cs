using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionApiException : Exception
{
    public SubscriptionApiException(int statusCode, string safeMessage, string errorCode)
        : base(safeMessage)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
}

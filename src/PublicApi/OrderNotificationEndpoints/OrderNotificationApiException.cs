using System;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class OrderNotificationApiException : Exception
{
    public OrderNotificationApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class NotificationApiException : Exception
{
    public NotificationApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

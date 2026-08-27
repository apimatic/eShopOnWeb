using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class NotificationOperationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

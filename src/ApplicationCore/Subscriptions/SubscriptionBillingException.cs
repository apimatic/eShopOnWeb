using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string code,
        string publicMessage,
        int statusCode,
        Exception? innerException = null)
        : base(publicMessage, innerException)
    {
        Code = code;
        PublicMessage = publicMessage;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public string PublicMessage { get; }
    public int StatusCode { get; }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionException : Exception
{
    public int? HttpStatusCode { get; }

    public SubscriptionException(string message, int? httpStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }
}

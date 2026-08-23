using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class BillingProviderException : BillingException
{
    public BillingProviderException(HttpStatusCode statusCode, string safeMessage, Exception? innerException = null)
        : base(statusCode, safeMessage, innerException)
    {
    }
}

public sealed class MaxioWriteResendBlockedException : Exception
{
    public MaxioWriteResendBlockedException()
        : base("A retry of a non-idempotent Maxio write was blocked.")
    {
    }
}

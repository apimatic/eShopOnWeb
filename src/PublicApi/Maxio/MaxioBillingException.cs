using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(
        string message,
        HttpStatusCode? providerStatusCode = null,
        bool outcomeMayBeAmbiguous = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeMayBeAmbiguous = outcomeMayBeAmbiguous;
    }

    public HttpStatusCode? ProviderStatusCode { get; }
    public bool OutcomeMayBeAmbiguous { get; }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

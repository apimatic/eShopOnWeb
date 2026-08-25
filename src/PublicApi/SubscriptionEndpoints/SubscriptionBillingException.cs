using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(HttpStatusCode statusCode, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal enum MaxioFailureKind
{
    ProviderResponse,
    Transport,
    MalformedResponse,
    AmbiguousWrite
}

internal sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(
        MaxioFailureKind kind,
        string safeMessage,
        HttpStatusCode? providerStatus = null,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
    }

    public MaxioFailureKind Kind { get; }
    public HttpStatusCode? ProviderStatus { get; }
}

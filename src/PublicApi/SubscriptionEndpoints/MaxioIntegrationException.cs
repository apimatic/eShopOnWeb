using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public enum MaxioFailureKind
{
    Configuration,
    Validation,
    Unavailable,
    InvalidResponse,
    AmbiguousWrite
}

public sealed class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(
        MaxioFailureKind kind,
        string safeMessage,
        Exception? innerException = null,
        HttpStatusCode? providerStatus = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
    }

    public MaxioFailureKind Kind { get; }
    public HttpStatusCode? ProviderStatus { get; }
}

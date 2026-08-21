using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingFailureKind
{
    Validation,
    NotFound,
    Conflict,
    Authentication,
    Configuration,
    Unavailable,
    UnknownOutcome
}

public sealed class BillingException : Exception
{
    public BillingException(
        BillingFailureKind kind,
        string safeMessage,
        HttpStatusCode? providerStatusCode = null,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }
    public HttpStatusCode? ProviderStatusCode { get; }
}

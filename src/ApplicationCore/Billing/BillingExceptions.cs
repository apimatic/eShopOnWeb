using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingFailureKind
{
    InvalidRequest,
    NotFound,
    Unavailable,
    Misconfigured,
    Indeterminate
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }
    public int? ProviderStatusCode { get; }
}

public sealed class BillingOperationInProgressException : Exception
{
    public BillingOperationInProgressException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingFailureKind
{
    InvalidRequest,
    NotFound,
    Conflict,
    ProviderUnavailable,
    ProviderFailure
}

public sealed class BillingException : Exception
{
    public BillingException(BillingFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public BillingFailureKind Kind { get; }
}

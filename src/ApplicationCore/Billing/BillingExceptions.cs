using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingErrorKind
{
    InvalidRequest,
    NotFound,
    Conflict,
    Validation,
    Configuration,
    SandboxRequired,
    UnauthorizedProvider,
    Throttled,
    Unavailable,
    InvalidProviderResponse
}

public sealed class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
    }

    public BillingErrorKind Kind { get; }
}


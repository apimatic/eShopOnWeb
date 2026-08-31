using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order or bill does not exist, or does not belong to the caller. The same
/// exception is used for "not found" and "not yours" so that one shopper cannot even learn of the
/// existence of another shopper's data. Surfaced as HTTP 404.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// The requested action is not valid for the bill in its current state — for example, correcting a
/// bill that has already been put to the shopper or withdrawn. Surfaced as HTTP 409. The caller is
/// told plainly rather than the change silently doing nothing.
/// </summary>
public class InvoiceOperationException : Exception
{
    public InvoiceOperationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The provider legitimately refused a state transition (for example, cancelling a paid bill, or
/// re-issuing a withdrawn one). This is an outcome of the bill's state at the provider, not an
/// integration fault. Surfaced as HTTP 409.
/// </summary>
public class ProviderOperationRefusedException : Exception
{
    public ProviderOperationRefusedException(string message) : base(message)
    {
    }

    public ProviderOperationRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

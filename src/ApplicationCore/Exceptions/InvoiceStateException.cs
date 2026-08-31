using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action cannot be performed because of the state the bill is in — for example
/// correcting a bill that has already been put to the shopper or withdrawn. This is an expected
/// outcome of the bill's lifecycle, not an integration defect.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message)
        : base(message)
    {
    }
}

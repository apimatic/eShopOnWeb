using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The invoicing provider could not be reached, or returned a failure that is not attributable to
/// the state of a specific bill (network error, authentication failure, unexpected 5xx, etc.).
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message) : base(message) { }
    public InvoiceProviderException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The provider legitimately refused a transition because of the state the bill is in — for example
/// a bill that has been withdrawn, or already put to the shopper, that will not accept a change a
/// fresh one would. This is an expected outcome of the bill's state, not an integration defect.
/// </summary>
public class InvoiceStateConflictException : Exception
{
    public InvoiceStateConflictException(string message) : base(message) { }
    public InvoiceStateConflictException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The provider has no record of the requested bill.</summary>
public class InvoiceNotFoundAtProviderException : Exception
{
    public InvoiceNotFoundAtProviderException(string message) : base(message) { }
}

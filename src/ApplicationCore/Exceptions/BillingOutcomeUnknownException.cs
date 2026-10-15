using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A write to the billing provider may or may not have taken effect, and re-reading the provider
/// by the write's reference could not settle it. Callers must not report this as a failure.
/// </summary>
public class BillingOutcomeUnknownException : Exception
{
    public BillingOutcomeUnknownException(string reference, Exception? innerException = null)
        : base("The billing provider did not confirm the outcome of the request.", innerException)
    {
        Reference = reference;
    }

    public string Reference { get; }
}

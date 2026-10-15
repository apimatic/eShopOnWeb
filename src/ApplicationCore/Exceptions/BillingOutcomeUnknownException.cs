using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A write to the billing provider may or may not have taken effect, and re-reading the provider did not settle it.
/// The local claim is kept in the Unknown state and the next request for it settles the outcome before doing anything else.
/// </summary>
public class BillingOutcomeUnknownException : BillingProviderException
{
    public BillingOutcomeUnknownException(string message, Exception? innerException = null)
        : base(BillingFailureKind.Unavailable, message, innerException: innerException)
    {
    }
}

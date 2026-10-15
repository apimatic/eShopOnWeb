using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A write to the billing provider failed after it may already have been applied, and re-reading the
/// provider did not settle it. The caller must record the outcome as unknown, not as failed.
/// </summary>
public class BillingOutcomeUnknownException : Exception
{
    public BillingOutcomeUnknownException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

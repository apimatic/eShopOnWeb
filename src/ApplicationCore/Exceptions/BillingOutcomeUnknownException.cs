using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A write was put on the wire but its outcome could not be established — the connection failed,
/// the call timed out, or the response could not be read. It may or may not have taken effect,
/// and reconciliation against the provider did not settle it.
/// This is deliberately distinct from <see cref="BillingUnavailableException"/>: a caller must
/// not blind-retry a write whose outcome is unknown.
/// </summary>
public class BillingOutcomeUnknownException : BillingException
{
    public BillingOutcomeUnknownException(string message, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException, providerStatusCode)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the configured billing entities do not resolve or do not match the shape the
/// integration requires (a stale handle, a re-seeded sandbox, or a component of the wrong kind).
/// This is a configuration fault, not a transient provider fault: retrying will not help until the
/// provider-side seed is corrected.
/// </summary>
public class BillingConfigurationException : BillingProviderException
{
    public BillingConfigurationException(string operation, string message)
        : base(operation, message)
    {
    }

    public BillingConfigurationException(string operation, string message, Exception innerException)
        : base(operation, message, null, innerException)
    {
    }
}

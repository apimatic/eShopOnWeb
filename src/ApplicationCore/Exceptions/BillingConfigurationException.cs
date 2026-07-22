using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the configured billing entities do not resolve or do not have the expected
/// shape — for example a stale product handle after a sandbox re-seed, or a component that is
/// not metered. The fix is always to correct the seed or the configuration, never to retry.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

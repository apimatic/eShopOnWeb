using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the integration's configured billing entities do not resolve — for example a
/// product handle that no longer exists after a sandbox re-seed, or a component that is not of
/// metered kind. This is a deployment/seed problem rather than a customer-facing failure, so it
/// is distinguished from <see cref="BillingProviderException"/>.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

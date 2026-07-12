using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A configured billing-provider handle/id does not resolve, or resolves to an entity of the
/// wrong shape (e.g. the metered component handle points at a non-metered component). Points
/// back at UC0 (seed the sandbox) rather than enrolling/recording usage against a guessed entity.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

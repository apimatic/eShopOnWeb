using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is misconfigured — a missing credential, or a configured handle that does
/// not resolve to the entity the integration expects. This is an operator problem, never a customer
/// one, so it must not be presented as a validation failure.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

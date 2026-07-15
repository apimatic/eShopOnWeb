using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a configured billing-provider handle (product family, product, or component) does not
/// resolve, or resolves to an entity of the wrong shape (e.g. a non-metered component). Surfaces as a
/// configuration error pointing back at UC0 (seed the sandbox) rather than a transient failure.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

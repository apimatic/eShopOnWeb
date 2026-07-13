using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A configured product/component handle does not resolve against the billing provider, or
/// resolves to an entity of the wrong shape (e.g. a non-metered component). Points back at UC0
/// (seed the sandbox) rather than a transient provider failure.
/// </summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

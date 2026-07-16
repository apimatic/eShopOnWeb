using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A configured Maxio handle (product family, plan, or metered component) does not resolve, or
/// resolves to an entity of the wrong shape. Points the caller back at UC0 (sandbox seeding).
/// </summary>
public class BillingConfigurationException : BillingProviderException
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

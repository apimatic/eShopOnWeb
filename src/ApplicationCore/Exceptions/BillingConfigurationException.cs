using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the configured billing entities (product family, plan or metered component handles) do not
/// resolve, or resolve to something of the wrong shape. Points the operator back at the sandbox seed.
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

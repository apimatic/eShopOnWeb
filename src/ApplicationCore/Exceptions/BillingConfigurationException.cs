using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>A configured product/family/component handle did not resolve at the provider — points back at UC0 (seed setup).</summary>
public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }

    public BillingConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

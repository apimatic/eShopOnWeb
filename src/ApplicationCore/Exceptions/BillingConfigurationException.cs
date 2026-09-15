using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingConfigurationException : Exception
{
    public BillingConfigurationException(string message) : base(message)
    {
    }
}

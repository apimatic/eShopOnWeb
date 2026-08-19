using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message) : base(message)
    {
    }
}

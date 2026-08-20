using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingValidationException : Exception
{
    public BillingValidationException(string message) : base(message)
    {
    }
}

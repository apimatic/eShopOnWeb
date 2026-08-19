using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingNotFoundException : Exception
{
    public BillingNotFoundException(string message) : base(message)
    {
    }
}

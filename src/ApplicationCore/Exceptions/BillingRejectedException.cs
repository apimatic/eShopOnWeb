using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The billing provider answered and refused the request; retrying it unchanged will not help.</summary>
public class BillingRejectedException : Exception
{
    public BillingRejectedException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

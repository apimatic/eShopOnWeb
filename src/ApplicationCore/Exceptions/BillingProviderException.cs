using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message) { }
    public BillingProviderException(string message, Exception innerException) : base(message, innerException) { }
}

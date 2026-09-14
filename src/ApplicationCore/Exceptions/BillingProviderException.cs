using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

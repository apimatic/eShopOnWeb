using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

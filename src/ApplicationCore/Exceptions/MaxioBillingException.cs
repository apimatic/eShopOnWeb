using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class MaxioBillingException : Exception
{
    public int StatusCode { get; }

    public MaxioBillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

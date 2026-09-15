using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

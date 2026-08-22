using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, int? httpStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
    }

    public int? HttpStatusCode { get; }
}

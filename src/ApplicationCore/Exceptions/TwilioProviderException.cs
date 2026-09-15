using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioProviderException : Exception
{
    public int StatusCode { get; }

    public TwilioProviderException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}

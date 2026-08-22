using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioUnavailableException : Exception
{
    public TwilioUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

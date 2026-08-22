using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioApiException : Exception
{
    public TwilioApiException(string message) : base(message)
    {
    }

    public TwilioApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

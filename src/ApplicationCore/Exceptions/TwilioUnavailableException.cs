using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioUnavailableException : Exception
{
    public TwilioUnavailableException()
        : base("The messaging provider is unavailable. Try again later.")
    {
    }

    public TwilioUnavailableException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string reason)
        : base($"The phone number is not a usable destination: {reason}")
    {
    }
}

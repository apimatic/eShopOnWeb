using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException()
        : base("The phone number is not a usable destination.")
    {
    }

    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusablePhoneNumberException : Exception
{
    public UnusablePhoneNumberException(string message) : base(message)
    {
    }
}

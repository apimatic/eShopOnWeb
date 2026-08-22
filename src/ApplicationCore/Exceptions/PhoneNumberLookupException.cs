using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PhoneNumberLookupException : Exception
{
    public PhoneNumberLookupException(string message) : base(message)
    {
    }

    public PhoneNumberLookupException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

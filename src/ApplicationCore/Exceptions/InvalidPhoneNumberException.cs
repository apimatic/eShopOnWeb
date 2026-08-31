using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the messaging provider does not consider a phone number a usable SMS destination.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

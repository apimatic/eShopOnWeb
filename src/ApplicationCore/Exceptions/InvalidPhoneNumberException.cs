using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a phone number the provider does not consider a usable destination is
/// registered (maps to HTTP 400). The offending number is never included in the message.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

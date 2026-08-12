using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a phone number the shopper tries to register is not a usable messaging
/// destination according to the messaging provider. The offending number is never
/// included in the message so it does not leak into logs.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException()
        : base("The supplied phone number is not a valid, reachable messaging destination.")
    {
    }
}

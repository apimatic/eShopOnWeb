using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a phone number the caller tried to register is not a usable destination according to
/// the messaging provider. Surfaced as HTTP 400 so it is rejected at registration time rather than at
/// the moment a message fails to go out. The rejected number is never echoed back.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string reason)
        : base($"The phone number is not a usable destination: {reason}")
    {
    }
}

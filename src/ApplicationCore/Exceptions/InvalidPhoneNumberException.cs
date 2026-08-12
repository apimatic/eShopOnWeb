using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a phone number the shopper tried to register is not one the messaging
/// provider considers a usable destination. Rejected up front, at registration time,
/// rather than when a message later fails to go out.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

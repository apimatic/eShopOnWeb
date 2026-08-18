using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A number the provider does not consider a usable destination was offered for registration.
/// Thrown at registration time so the number is rejected before any message is ever attempted.
/// The message is caller-safe and never echoes the offending number.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

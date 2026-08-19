using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a phone number the caller tried to register is not a usable
/// destination according to the messaging provider. The message must never
/// contain the phone number itself (numbers are PII and are never logged/echoed).
/// </summary>
public class PhoneNumberValidationException : Exception
{
    public PhoneNumberValidationException(string message) : base(message)
    {
    }
}

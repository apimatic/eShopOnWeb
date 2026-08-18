using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to register a number the messaging provider does not consider a
/// usable destination. Rejected at registration time rather than when a later message fails.
/// The offending number is never included in the message (it is PII and must not be logged).
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message) : base(message)
    {
    }
}

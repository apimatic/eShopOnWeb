using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to register a number the provider does not consider a usable
/// destination. Surfaced as HTTP 400 so the number is rejected at registration time, not later
/// when a message fails to go out.
/// </summary>
public class PhoneNumberNotUsableException : Exception
{
    public PhoneNumberNotUsableException(string message) : base(message) { }
}

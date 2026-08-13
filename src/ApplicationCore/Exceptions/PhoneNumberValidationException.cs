using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper-supplied phone number is not a usable destination according to the
/// messaging provider, so it is rejected at registration time rather than at send time.
/// Surfaces to callers as HTTP 400 Bad Request.
/// </summary>
public class PhoneNumberValidationException : Exception
{
    public PhoneNumberValidationException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to register a number the provider does not consider a usable destination.
/// Rejected at registration time, rather than when a later message fails to go out.
/// </summary>
public class ContactNumberRejectedException : Exception
{
    public ContactNumberRejectedException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a contact number is not a usable messaging destination according to the provider.
/// The offending number is deliberately never included in the message.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

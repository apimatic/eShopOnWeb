using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a number a shopper tried to register is not a usable messaging destination.
/// The message never contains the rejected number.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException()
        : base("The supplied number was rejected by the messaging provider as an unusable destination.") { }
}

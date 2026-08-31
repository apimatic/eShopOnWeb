using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The messaging provider does not consider the supplied number a usable destination.
/// </summary>
public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

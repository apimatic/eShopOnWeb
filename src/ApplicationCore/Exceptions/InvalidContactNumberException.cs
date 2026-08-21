using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The number is not a usable destination.")
    {
    }

    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

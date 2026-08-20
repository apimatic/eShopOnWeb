using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The phone number is not a usable destination.")
    {
    }

    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

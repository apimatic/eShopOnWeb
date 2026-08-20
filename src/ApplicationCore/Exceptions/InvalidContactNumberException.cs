using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The provider does not consider this a usable destination number.")
    {
    }

    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The provider does not consider this number a usable destination.")
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException()
        : base("The mobile number is not a usable destination.")
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusableDestinationException : Exception
{
    public UnusableDestinationException()
        : base("The number is not a usable destination.")
    {
    }
}

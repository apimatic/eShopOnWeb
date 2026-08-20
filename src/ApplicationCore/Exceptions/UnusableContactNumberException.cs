using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusableContactNumberException : Exception
{
    public UnusableContactNumberException(string message) : base(message)
    {
    }
}

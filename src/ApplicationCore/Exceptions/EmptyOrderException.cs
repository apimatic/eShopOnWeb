using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EmptyOrderException : Exception
{
    public EmptyOrderException() : base("An order must contain at least one item.")
    {
    }
}

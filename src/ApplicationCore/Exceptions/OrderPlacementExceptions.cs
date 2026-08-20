using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EmptyOrderException : Exception
{
    public EmptyOrderException() : base("An order must contain at least one catalog item.")
    {
    }
}

public class CatalogItemNotFoundException : Exception
{
    public CatalogItemNotFoundException() : base("One or more catalog items were not found.")
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class EmptyCatalogOrderException : Exception
{
    public EmptyCatalogOrderException()
        : base("An order must include at least one catalog item with a positive quantity.")
    {
    }
}

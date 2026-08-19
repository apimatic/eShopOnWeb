using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a supplier's product listing cannot be read (e.g. the Firecrawl extract job errored,
/// timed out, or ended in a non-completed state).
/// </summary>
public class SupplierCatalogReadException : Exception
{
    public SupplierCatalogReadException(string message) : base(message)
    {
    }

    public SupplierCatalogReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a supplier's product listing cannot be read (e.g. the reader service rejected the
/// request, timed out, or returned an unusable response).
/// </summary>
public class SupplierCatalogReadException : Exception
{
    public SupplierCatalogReadException(string message) : base(message)
    {
    }

    public SupplierCatalogReadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

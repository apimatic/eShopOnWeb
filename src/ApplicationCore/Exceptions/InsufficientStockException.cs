using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order asks for more units of a catalog item than eShop currently holds.
/// </summary>
public class InsufficientStockException : Exception
{
    public int CatalogItemId { get; }
    public int Available { get; }
    public int Requested { get; }

    public InsufficientStockException(int catalogItemId, int available, int requested)
        : base($"Catalog item {catalogItemId} has {available} in stock but {requested} were requested")
    {
        CatalogItemId = catalogItemId;
        Available = available;
        Requested = requested;
    }

    public InsufficientStockException(string message) : base(message)
    {
    }

    public InsufficientStockException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

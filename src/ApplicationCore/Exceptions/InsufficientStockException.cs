using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order asks for more of a catalog item than eShop currently holds in stock.
/// </summary>
public class InsufficientStockException : Exception
{
    public InsufficientStockException(int catalogItemId, int available, int requested)
        : base($"Insufficient stock for catalog item {catalogItemId}: {available} available, {requested} requested.")
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

    public int CatalogItemId { get; }
    public int Available { get; }
    public int Requested { get; }
}

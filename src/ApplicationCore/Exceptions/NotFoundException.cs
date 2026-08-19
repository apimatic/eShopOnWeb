using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for "the requested entity does not exist" errors. The API boundary maps these to
/// HTTP 404.
/// </summary>
public abstract class NotFoundException : Exception
{
    protected NotFoundException(string message) : base(message)
    {
    }
}

public class SupplierNotFoundException : NotFoundException
{
    public SupplierNotFoundException(int supplierId)
        : base($"No supplier was found with id {supplierId}.")
    {
    }
}

public class SupplierSyncNotFoundException : NotFoundException
{
    public SupplierSyncNotFoundException(int syncId)
        : base($"No sync was found with id {syncId}.")
    {
    }
}

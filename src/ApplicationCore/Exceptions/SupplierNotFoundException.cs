using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SupplierNotFoundException : Exception
{
    public SupplierNotFoundException(int supplierId)
        : base($"No supplier exists with id {supplierId}")
    {
    }
}

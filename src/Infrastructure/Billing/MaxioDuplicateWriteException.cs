using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioDuplicateWriteException : Exception
{
    public MaxioDuplicateWriteException()
        : base("A second send of a non-idempotent Maxio write was refused.")
    {
    }
}

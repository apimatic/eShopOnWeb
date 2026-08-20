using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate provider write was blocked.")
    {
    }
}

using System;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioDuplicateWritePreventedException : Exception
{
    public TwilioDuplicateWritePreventedException()
        : base("A duplicate provider write was blocked.")
    {
    }
}

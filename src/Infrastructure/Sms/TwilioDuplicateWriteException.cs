using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate messaging write was blocked before it reached the provider.")
    {
    }
}

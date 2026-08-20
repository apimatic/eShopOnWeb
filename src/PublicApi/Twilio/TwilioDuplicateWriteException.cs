using System;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate write to the messaging provider was blocked.")
    {
    }
}

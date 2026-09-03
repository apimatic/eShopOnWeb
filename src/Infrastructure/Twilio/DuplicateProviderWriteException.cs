using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A duplicate provider write was blocked.")
    {
    }
}

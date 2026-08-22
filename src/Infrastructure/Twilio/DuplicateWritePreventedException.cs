using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class DuplicateWritePreventedException : Exception
{
    public DuplicateWritePreventedException()
        : base("A duplicate provider write was prevented.")
    {
    }
}

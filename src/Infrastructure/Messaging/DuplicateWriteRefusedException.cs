using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A duplicate write to the messaging provider was refused.")
    {
    }
}

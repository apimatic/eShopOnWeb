using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A write was already sent to the billing provider.")
    {
    }
}

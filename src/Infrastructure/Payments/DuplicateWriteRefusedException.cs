using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A PayPal write was not resent after a transport failure. The previous attempt may already have been received.")
    {
    }
}

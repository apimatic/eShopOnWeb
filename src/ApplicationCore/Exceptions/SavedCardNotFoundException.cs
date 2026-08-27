using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int paymentMethodId)
        : base($"Payment method {paymentMethodId} was not found.") {}
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SavedCardNotFoundException : Exception
{
    public SavedCardNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}")
    {
    }
}

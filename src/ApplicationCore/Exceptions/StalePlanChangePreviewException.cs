using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration quoted to the customer no longer matches what the provider would
/// charge at commit time. The change is refused so the customer is never charged an amount other
/// than the one they confirmed; the caller must obtain a fresh preview.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException(string message)
        : base(message)
    {
    }
}

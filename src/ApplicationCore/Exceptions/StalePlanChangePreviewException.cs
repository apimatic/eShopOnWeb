using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the proration quoted to the customer no longer matches a freshly taken preview
/// at commit time. Per UC3 the change is refused rather than applied at a different amount.
/// </summary>
public class StalePlanChangePreviewException : Exception
{
    public StalePlanChangePreviewException()
        : base("The previewed cost of this plan change is no longer valid. Please review a fresh preview before confirming.")
    {
    }
}
